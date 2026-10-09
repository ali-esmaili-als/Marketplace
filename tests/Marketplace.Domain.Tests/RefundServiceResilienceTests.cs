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
    [Fact]
    public async Task Definitive_gateway_rejection_marks_refund_failed_and_allows_a_new_attempt()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(120, 130, 140, 150, 500_000, 500_000);
        order.MarkPaid(now);
        order.MarkReady();
        order.SetDeliveryExpiry(now.AddMinutes(1));
        order.MarkDeliveryExpired(now.AddMinutes(2));
        order.RequestRefund();

        var payment = Payment.Create(160, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-160");
        payment.Succeed("BANK-160");

        Refund? activeRefund = null;
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetActiveRefundByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => activeRefund?.Status == RefundStatus.Processing ? activeRefund : null);
        lifecycle.Setup(x => x.AddRefund(It.IsAny<Refund>()))
            .Callback<Refund>(refund => activeRefund = refund);
        lifecycle.Setup(x => x.GetRefundAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) =>
                activeRefund?.Id == id ? activeRefund : null);

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

        long nextId = 200;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupSequence(x => x.RefundAsync(
                payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(false);
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        gatewayFactory.Setup(x => x.GetForExistingPaymentAsync(
                PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);

        var service = new RefundService(
            orders.Object, payments.Object, lifecycle.Object, uow.Object, ids.Object, gatewayFactory.Object);

        await service.ProcessAsync(order.Id, RefundReason.DeliveryExpired);
        Assert.NotNull(activeRefund);
        Assert.Equal(RefundStatus.Failed, activeRefund!.Status);
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        await service.ProcessAsync(order.Id, RefundReason.DeliveryExpired);
        Assert.NotNull(activeRefund);
        Assert.Equal(RefundStatus.Failed, activeRefund!.Status);
        gateway.Verify(x => x.RefundAsync(
            payment.ReferenceNumber, order.TotalAmountIRR, It.IsAny<CancellationToken>()), Times.Exactly(2));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task Reconcile_confirmed_refund_finalizes_order_payment_hold_ledger_and_audit_once()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(220, 230, 240, 250, 500_000, 500_000);
        order.MarkPaid(now);
        order.MarkReady();
        order.SetDeliveryExpiry(now.AddMinutes(1));
        order.MarkDeliveryExpired(now.AddMinutes(2));
        order.RequestRefund();

        var payment = Payment.Create(260, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-260");
        payment.Succeed("BANK-260");

        var refund = Refund.Create(270, order.Id, payment.Id, order.CustomerId, order.TotalAmountIRR, RefundReason.DeliveryExpired);
        refund.Approve();
        refund.StartProcessing();

        var balance = SellerBalance.Create(280, order.SellerId);
        balance.AddAvailable(900_000);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(290, order.SellerId, order.Id, order.SellerAmountIRR, "order protection");

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetRefundAsync(refund.Id, It.IsAny<CancellationToken>())).ReturnsAsync(refund);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);

        var commission = Commission.Create(315, order.Id, 250, order.SellerId, order.TotalAmountIRR, 10m, 0);
        lifecycle.Setup(x => x.GetCommissionByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(commission);

        BalanceTransaction? transaction = null;
        CommissionReversal? reversal = null;
        RefundReconciliationAudit? audit = null;
        lifecycle.Setup(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()))
            .Callback<BalanceTransaction>(x => transaction = x);
        lifecycle.Setup(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()))
            .Callback<CommissionReversal>(x => reversal = x);
        lifecycle.Setup(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()))
            .Callback<RefundReconciliationAudit>(x => audit = x);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 300;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var service = new RefundService(
            orders.Object, payments.Object, lifecycle.Object, uow.Object, ids.Object,
            Mock.Of<IPaymentGatewayFactory>());

        await service.ReconcileAsync(refund.Id, adminUserId: 310, transferCompleted: true,
            bankReference: " BANK-REFUND-270 ", note: "Confirmed refund in provider portal");

        Assert.Equal(RefundStatus.Completed, refund.Status);
        Assert.Equal("BANK-REFUND-270", refund.ProviderReference);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(BalanceHoldStatus.Consumed, hold.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(400_000, balance.AvailableIRR);

        Assert.NotNull(transaction);
        Assert.Equal(BalanceTransactionType.Refund, transaction!.Type);
        Assert.Equal(BalanceBucket.Blocked, transaction.Bucket);
        Assert.Equal(order.Id, transaction.OrderId);
        Assert.Equal(refund.Id, transaction.RefundId);
        Assert.Equal(order.SellerAmountIRR, transaction.AmountIRR);
        Assert.Equal(500_000, transaction.BalanceBeforeIRR);
        Assert.Equal(0, transaction.BalanceAfterIRR);

        Assert.NotNull(reversal);
        Assert.Equal(commission.Id, reversal!.CommissionId);
        Assert.Equal(order.Id, reversal.OrderId);
        Assert.Equal(refund.Id, reversal.RefundId);
        Assert.Equal(refund.AmountIRR, reversal.RefundAmountIRR);
        Assert.Equal(commission.CommissionAmountIRR, reversal.ReversedCommissionIRR);

        Assert.NotNull(audit);
        Assert.Equal(refund.Id, audit!.RefundId);
        Assert.Equal(310, audit.AdminUserId);
        Assert.True(audit.TransferCompleted);
        Assert.Equal("BANK-REFUND-270", audit.BankReference);
        Assert.Equal("Confirmed refund in provider portal", audit.Note);

        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        lifecycle.Verify(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()), Times.Once);
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<DomainException>(() => service.ReconcileAsync(refund.Id, 311, true,
            "BANK-REFUND-270", "Duplicate reconciliation"));
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        lifecycle.Verify(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()), Times.Once);
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_confirmed_not_refunded_marks_refund_failed_and_keeps_order_and_payment_unmodified()
    {
        var refund = Refund.Create(410, 420, 430, 440, 150_000, RefundReason.DeliveryExpired);
        refund.Approve();
        refund.StartProcessing();

        RefundReconciliationAudit? audit = null;
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetRefundAsync(refund.Id, It.IsAny<CancellationToken>())).ReturnsAsync(refund);
        lifecycle.Setup(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()))
            .Callback<RefundReconciliationAudit>(x => audit = x);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = new RefundService(
            Mock.Of<IOrderRepository>(), Mock.Of<IPaymentRepository>(), lifecycle.Object, uow.Object,
            Mock.Of<IIdGenerator>(), Mock.Of<IPaymentGatewayFactory>());

        await service.ReconcileAsync(refund.Id, adminUserId: 450, transferCompleted: false,
            bankReference: null, note: "Provider confirms no refund was sent");

        Assert.Equal(RefundStatus.Failed, refund.Status);
        Assert.Equal("Provider confirms no refund was sent", refund.FailureReason);
        Assert.NotNull(audit);
        Assert.Equal(450, audit!.AdminUserId);
        Assert.False(audit.TransferCompleted);
        Assert.Null(audit.BankReference);
        Assert.Equal("Provider confirms no refund was sent", audit.Note);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        lifecycle.Verify(x => x.AddCommissionReversal(It.IsAny<CommissionReversal>()), Times.Never);
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<DomainException>(() => service.ReconcileAsync(refund.Id, 451, false,
            null, "Duplicate reconciliation"));
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

}
