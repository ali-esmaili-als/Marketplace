using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementReconciliationSafetyTests
{
    [Fact]
    public async Task Reconcile_not_transferred_releases_reservation_and_records_auditable_failure_once()
    {
        var settlement = Settlement.Create(101, 202, 400_000, 303, "Bank", "IR00303", "Seller");
        settlement.MarkProcessing();
        settlement.PutOnHold();

        var balance = SellerBalance.Create(404, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        SettlementReconciliationAudit? capturedAudit = null;
        BalanceTransaction? capturedTransaction = null;
        lifecycle.Setup(x => x.AddSettlementReconciliationAudit(It.IsAny<SettlementReconciliationAudit>()))
            .Callback<SettlementReconciliationAudit>(audit => capturedAudit = audit);
        lifecycle.Setup(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()))
            .Callback<BalanceTransaction>(transaction => capturedTransaction = transaction);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 700;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object, Mock.Of<ISellerPayoutGateway>(), Mock.Of<ISellerManagementRepository>());

        var result = await service.ReconcileAsync(
            settlement.Id, adminUserId: 909, transferCompleted: false, bankReference: null,
            note: "Confirmed not transferred by bank statement");

        Assert.Equal("Failed", result.Status);
        Assert.Equal(SettlementStatus.Failed, settlement.Status);
        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1_000_000, balance.WithdrawableIRR);

        Assert.NotNull(capturedAudit);
        Assert.Equal(909, capturedAudit!.AdminUserId);
        Assert.Equal(settlement.Id, capturedAudit.SettlementId);
        Assert.False(capturedAudit.TransferCompleted);
        Assert.Equal("Confirmed not transferred by bank statement", capturedAudit.Note);

        Assert.NotNull(capturedTransaction);
        Assert.Equal(BalanceTransactionType.SettlementFailed, capturedTransaction!.Type);
        Assert.Equal(400_000, capturedTransaction.AmountIRR);
        Assert.Equal(400_000, capturedTransaction.BalanceBeforeIRR);
        Assert.Equal(0, capturedTransaction.BalanceAfterIRR);
        Assert.Equal(BalanceBucket.ReservedForSettlement, capturedTransaction.Bucket);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // A second admin action cannot release the same reservation a second time.
        await Assert.ThrowsAsync<DomainException>(() => service.ReconcileAsync(
            settlement.Id, 910, false, null, "Attempt duplicate reconciliation"));
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1, capturedAudit.AdminUserId == 909 ? 1 : 0);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_paid_requires_bank_reference_before_changing_financial_state()
    {
        var settlement = Settlement.Create(111, 222, 250_000, 333, "Bank", "IR00333", "Seller");
        settlement.MarkProcessing();
        settlement.PutOnHold();

        var balance = SellerBalance.Create(444, settlement.SellerId);
        balance.AddAvailable(700_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, Mock.Of<IIdGenerator>(), Mock.Of<ISellerPayoutGateway>(), Mock.Of<ISellerManagementRepository>());

        await Assert.ThrowsAsync<DomainException>(() => service.ReconcileAsync(
            settlement.Id, 909, transferCompleted: true, bankReference: "  ",
            note: "Bank confirmed transfer"));

        Assert.Equal(SettlementStatus.OnHold, settlement.Status);
        Assert.Equal(700_000, balance.AvailableIRR);
        Assert.Equal(250_000, balance.ReservedForSettlementIRR);
        lifecycle.Verify(x => x.AddSettlementReconciliationAudit(It.IsAny<SettlementReconciliationAudit>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
