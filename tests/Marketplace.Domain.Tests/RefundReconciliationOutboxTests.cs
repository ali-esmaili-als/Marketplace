using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Refunds;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class RefundReconciliationOutboxTests
{
    [Fact]
    public async Task Reconcile_not_transferred_records_audit_and_outbox_in_same_transaction()
    {
        var refund = Refund.Create(101, 202, 303, 404, 500_000, RefundReason.DeliveryExpired);
        refund.Approve();
        refund.StartProcessing();

        RefundReconciliationAudit? capturedAudit = null;
        OutboxMessage? capturedOutbox = null;
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetRefundAsync(refund.Id, It.IsAny<CancellationToken>())).ReturnsAsync(refund);
        lifecycle.Setup(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()))
            .Callback<RefundReconciliationAudit>(x => capturedAudit = x);
        lifecycle.Setup(x => x.AddOutboxMessage(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(x => capturedOutbox = x);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 500;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var service = new RefundService(
            Mock.Of<IOrderRepository>(), Mock.Of<IPaymentRepository>(), lifecycle.Object,
            uow.Object, ids.Object, Mock.Of<IPaymentGatewayFactory>());

        await service.ReconcileAsync(refund.Id, adminUserId: 909, transferCompleted: false,
            bankReference: " BANK-CHECK-101 ", note: " Bank statement confirms no transfer ");

        Assert.Equal(RefundStatus.Failed, refund.Status);
        Assert.NotNull(capturedAudit);
        Assert.Equal(909, capturedAudit!.AdminUserId);
        Assert.Equal("Bank statement confirms no transfer", capturedAudit.Note);
        Assert.Equal("BANK-CHECK-101", capturedAudit.BankReference);
        Assert.False(capturedAudit.TransferCompleted);
        Assert.NotNull(capturedOutbox);
        Assert.Equal("Refund.Reconciled", capturedOutbox!.EventType);
        Assert.Contains("\"TransferCompleted\":false", capturedOutbox.PayloadJson);
        Assert.Contains("BANK-CHECK-101", capturedOutbox.PayloadJson);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<Marketplace.Domain.Common.DomainException>(() =>
            service.ReconcileAsync(refund.Id, adminUserId: 910, transferCompleted: false,
                bankReference: null, note: "Duplicate attempt"));
        lifecycle.Verify(x => x.AddRefundReconciliationAudit(It.IsAny<RefundReconciliationAudit>()), Times.Once);
        lifecycle.Verify(x => x.AddOutboxMessage(It.IsAny<OutboxMessage>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
