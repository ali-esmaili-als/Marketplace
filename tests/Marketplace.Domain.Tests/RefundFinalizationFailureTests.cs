using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class RefundFinalizationFailureTests
{
    [Fact]
    public async Task Successful_gateway_refund_with_database_finalization_failure_remains_processing_for_reconciliation()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(601, 602, 603, 604, 500_000, 500_000);
        order.MarkPaid(now);
        order.MarkReady();
        order.SetDeliveryExpiry(now.AddMinutes(1));
        order.MarkDeliveryExpired(now.AddMinutes(2));
        order.RequestRefund();

        var payment = Payment.Create(605, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-605");
        payment.Succeed("BANK-605");

        Refund? createdRefund = null;
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetActiveRefundByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (Refund?)null);
        lifecycle.Setup(x => x.AddRefund(It.IsAny<Refund>()))
            .Callback<Refund>(refund => createdRefund = refund);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var transactionCalls = 0;
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) =>
            {
                transactionCalls++;
                return transactionCalls == 1
                    ? action(token)
                    : Task.FromException<int>(new InvalidOperationException("Refund finalization commit failed."));
            });
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 700;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.RefundAsync(payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundResult(true, "BANK-REFUND-601", null));
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        gatewayFactory.Setup(x => x.GetForExistingPaymentAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);

        var service = new RefundService(
            orders.Object, payments.Object, lifecycle.Object, uow.Object, ids.Object, gatewayFactory.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(order.Id, RefundReason.DeliveryExpired));

        Assert.Equal("Refund finalization commit failed.", error.Message);
        Assert.NotNull(createdRefund);
        Assert.Equal(RefundStatus.Processing, createdRefund!.Status);
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        lifecycle.Verify(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()), Times.Never);
        gateway.Verify(x => x.RefundAsync(payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, transactionCalls);
    }
}
