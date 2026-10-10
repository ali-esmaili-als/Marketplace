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

public sealed class RefundFinancialPreflightTests
{
    [Fact]
    public async Task Reconcile_with_mismatched_hold_rejects_before_mutating_any_financial_aggregate()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(1601, 1602, 1603, 1604, 500_000, 500_000);
        order.MarkPaid(now);
        order.MarkReady();
        order.SetDeliveryExpiry(now.AddMinutes(1));
        order.MarkDeliveryExpired(now.AddMinutes(2));
        order.RequestRefund();

        var payment = Payment.Create(1605, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-1605");
        payment.Succeed("BANK-1605");

        var refund = Refund.Create(1606, order.Id, payment.Id, order.CustomerId,
            order.TotalAmountIRR, RefundReason.DeliveryExpired);
        refund.Approve();
        refund.StartProcessing();

        var balance = SellerBalance.Create(1607, order.SellerId);
        balance.AddAvailable(900_000);
        balance.Block(order.SellerAmountIRR);
        var mismatchedHold = SellerBalanceHold.Create(
            1608, order.SellerId + 1, order.Id, order.SellerAmountIRR, "deliberately mismatched seller");

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetRefundAsync(refund.Id, It.IsAny<CancellationToken>())).ReturnsAsync(refund);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mismatchedHold);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = new RefundService(
            orders.Object, payments.Object, lifecycle.Object, uow.Object,
            Mock.Of<IIdGenerator>(), Mock.Of<IPaymentGatewayFactory>());

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.ReconcileAsync(refund.Id, adminUserId: 1609, transferCompleted: true,
                bankReference: "BANK-REFUND-1606", note: "Provider confirms transfer"));

        Assert.Contains("does not match", error.Message);
        Assert.Equal(RefundStatus.Processing, refund.Status);
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(500_000, balance.BlockedIRR);
        Assert.Equal(400_000, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Active, mismatchedHold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        lifecycle.Verify(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()), Times.Never);
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
