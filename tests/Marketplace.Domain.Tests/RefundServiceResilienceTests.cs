using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

/// <summary>
/// Regression coverage for ambiguous gateway outcomes. These are service unit tests with mocked persistence,
/// not SQL Server integration tests.
/// </summary>
public sealed class RefundServiceResilienceTests
{
    [Fact]
    public async Task Gateway_timeout_keeps_refund_processing_and_blocks_duplicate_refund_attempt()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(20, 30, 40, 50, 500_000, 500_000);
        order.MarkPaid(now);
        order.MarkReady();
        order.SetDeliveryExpiry(now.AddMinutes(1));
        order.MarkDeliveryExpired(now.AddMinutes(2));
        order.RequestRefund();

        var payment = Payment.Create(60, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-60");
        payment.Succeed("BANK-60");

        Refund? createdRefund = null;
        var refundLookupCount = 0;
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetActiveRefundByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(++refundLookupCount == 1 ? (Refund?)null : createdRefund));
        lifecycle.Setup(x => x.AddRefund(It.IsAny<Refund>()))
            .Callback<Refund>(refund => createdRefund = refund);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 100;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.RefundAsync(payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Gateway timed out after refund request submission."));
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        gatewayFactory.Setup(x => x.GetForExistingPaymentAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);

        var service = new RefundService(
            orders.Object, payments.Object, lifecycle.Object, uow.Object, ids.Object, gatewayFactory.Object);

        await Assert.ThrowsAsync<TimeoutException>(() => service.ProcessAsync(order.Id, RefundReason.DeliveryExpired));

        Assert.NotNull(createdRefund);
        Assert.Equal(RefundStatus.Processing, createdRefund!.Status);
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        gateway.Verify(x => x.RefundAsync(payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()), Times.Once);

        // The gateway outcome is ambiguous. A retry must stop at the active-refund guard,
        // rather than sending a second refund request to the bank.
        await Assert.ThrowsAsync<DomainException>(() => service.ProcessAsync(order.Id, RefundReason.DeliveryExpired));

        gateway.Verify(x => x.RefundAsync(payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
