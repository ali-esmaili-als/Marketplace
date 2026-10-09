using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Sellers;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementServiceTests
{
    [Fact]
    public async Task Request_with_same_idempotency_key_returns_existing_settlement_without_reserving_funds_twice()
    {
        var seller = Seller.Create(701, 702);
        seller.Activate();
        var existing = Settlement.Create(703, seller.Id, 400_000, 704,
            "Test Bank", "IR0704", "Seller", "checkout-2026-01");
        var lifecycle = new Mock<ILifecycleRepository>(MockBehavior.Strict);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(seller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SellerBalance?)null);
        lifecycle.Setup(x => x.GetSettlementByRequestKeyAsync(
                seller.Id, "checkout-2026-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sellers = new Mock<ISellerManagementRepository>(MockBehavior.Strict);
        sellers.Setup(x => x.GetSellerByUserIdAsync(seller.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(),
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        var result = await service.RequestAsync(seller.UserId, existing.BankAccountId,
            existing.AmountIRR, "checkout-2026-01");

        Assert.Equal(existing.Id, result.SettlementId);
        Assert.Equal(existing.AmountIRR, result.AmountIRR);
        Assert.Equal("Requested", result.Status);
        lifecycle.Verify(x => x.AddSettlement(It.IsAny<Settlement>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.VerifyAll();
        sellers.VerifyAll();
        uow.VerifyAll();
    }

    [Fact]
    public async Task Request_rejects_reusing_idempotency_key_with_different_amount()
    {
        var seller = Seller.Create(711, 712);
        seller.Activate();
        var existing = Settlement.Create(713, seller.Id, 400_000, 714,
            "Test Bank", "IR0714", "Seller", "checkout-2026-02");
        var lifecycle = new Mock<ILifecycleRepository>(MockBehavior.Strict);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(seller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SellerBalance?)null);
        lifecycle.Setup(x => x.GetSettlementByRequestKeyAsync(
                seller.Id, "checkout-2026-02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sellers = new Mock<ISellerManagementRepository>(MockBehavior.Strict);
        sellers.Setup(x => x.GetSellerByUserIdAsync(seller.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(),
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        await Assert.ThrowsAsync<Marketplace.Domain.Common.DomainException>(() =>
            service.RequestAsync(seller.UserId, existing.BankAccountId, existing.AmountIRR + 1, "checkout-2026-02"));

        lifecycle.Verify(x => x.AddSettlement(It.IsAny<Settlement>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.VerifyAll();
        sellers.VerifyAll();
        uow.VerifyAll();
    }

    [Fact]
    public async Task Concurrent_processing_requests_for_same_settlement_call_payout_only_once()
    {
        var settlement = Settlement.Create(700, 701, 400_000, 702, "Test Bank", "IR0702", "Seller");
        var balance = SellerBalance.Create(703, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var payoutStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPayout = new TaskCompletionSource<(bool Success, string? Reference, string? Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                payoutStarted.TrySetResult();
                return finishPayout.Task;
            });

        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(), payout.Object, Mock.Of<ISellerManagementRepository>());

        var first = service.ProcessAsync(settlement.Id);
        await payoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<Marketplace.Domain.Common.DomainException>(
            () => service.ProcessAsync(settlement.Id));

        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);

        finishPayout.SetResult((true, "BANK-ONCE", (string?)null));
        var result = await first;

        Assert.Equal("Completed", result.Status);
        Assert.Equal(600_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessingAlreadyCompletedSettlement_DoesNotCallPayoutGatewayAgain()
    {
        var settlement = Settlement.Create(1, 2, 400_000, 3, "Test Bank", "IR0001", "Seller");
        settlement.MarkProcessing();
        settlement.Complete("BANK-REF");

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));

        var payout = new Mock<ISellerPayoutGateway>(MockBehavior.Strict);
        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(), payout.Object, Mock.Of<ISellerManagementRepository>());

        var result = await service.ProcessAsync(settlement.Id);

        Assert.Equal("Completed", result.Status);
        Assert.Equal("BANK-REF", result.Reference);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AmbiguousPayoutFailure_PutsSettlementOnHoldAndKeepsFundsReserved()
    {
        var settlement = Settlement.Create(10, 20, 400_000, 30, "Test Bank", "IR0030", "Seller");
        var balance = SellerBalance.Create(40, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Gateway timed out after request submission."));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(), payout.Object, Mock.Of<ISellerManagementRepository>());

        await Assert.ThrowsAsync<TimeoutException>(() => service.ProcessAsync(settlement.Id));

        Assert.Equal(SettlementStatus.OnHold, settlement.Status);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
    [Fact]
    public async Task OnHoldSettlement_CannotBeRetriedBeforeReconciliation()
    {
        var settlement = Settlement.Create(41, 42, 300_000, 43, "Test Bank", "IR0043", "Seller");
        settlement.MarkProcessing();
        settlement.PutOnHold();

        var balance = SellerBalance.Create(44, settlement.SellerId);
        balance.AddAvailable(800_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));

        var payout = new Mock<ISellerPayoutGateway>(MockBehavior.Strict);
        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(), payout.Object, Mock.Of<ISellerManagementRepository>());

        await Assert.ThrowsAsync<Marketplace.Domain.Common.DomainException>(
            () => service.ProcessAsync(settlement.Id));

        Assert.Equal(SettlementStatus.OnHold, settlement.Status);
        Assert.Equal(800_000, balance.AvailableIRR);
        Assert.Equal(300_000, balance.ReservedForSettlementIRR);
        Assert.Equal(500_000, balance.WithdrawableIRR);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DefinitivePayoutFailure_ReleasesReservationAndWritesFailureLedgerExactlyOnce()
    {
        var settlement = Settlement.Create(45, 46, 400_000, 47, "Test Bank", "IR0047", "Seller");
        var balance = SellerBalance.Create(48, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        BalanceTransaction? capturedTransaction = null;
        lifecycle.Setup(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()))
            .Callback<BalanceTransaction>(transaction => capturedTransaction = transaction);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(49);
        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (string?)null, "Bank definitively rejected transfer"));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object, payout.Object, Mock.Of<ISellerManagementRepository>());

        var result = await service.ProcessAsync(settlement.Id);

        Assert.Equal("Failed", result.Status);
        Assert.Equal(SettlementStatus.Failed, settlement.Status);
        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1_000_000, balance.WithdrawableIRR);

        Assert.NotNull(capturedTransaction);
        Assert.Equal(BalanceTransactionType.SettlementFailed, capturedTransaction!.Type);
        Assert.Equal(BalanceBucket.ReservedForSettlement, capturedTransaction.Bucket);
        Assert.Equal(settlement.Id, capturedTransaction.SettlementId);
        Assert.Equal(400_000, capturedTransaction.AmountIRR);
        Assert.Equal(400_000, capturedTransaction.BalanceBeforeIRR);
        Assert.Equal(0, capturedTransaction.BalanceAfterIRR);
        Assert.Equal("Bank definitively rejected transfer", capturedTransaction.Reference);

        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SuccessfulSettlement_DeductsAvailableAndClearsReservationExactlyOnce()
    {
        var settlement = Settlement.Create(50, 60, 400_000, 70, "Test Bank", "IR0070", "Seller");
        var balance = SellerBalance.Create(80, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(90);
        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "BANK-PAID-50", (string?)null));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object, payout.Object, Mock.Of<ISellerManagementRepository>());

        var result = await service.ProcessAsync(settlement.Id);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(600_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.Is<BalanceTransaction>(t =>
            t.Type == BalanceTransactionType.Settlement && t.AmountIRR == 400_000 &&
            t.BalanceBeforeIRR == 1_000_000 && t.BalanceAfterIRR == 600_000)), Times.Once);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }


    [Fact]
    public async Task Reconcile_CompletedTransferWritesAuditWithAdminIdentityAndKeepsLedgerConsistent()
    {
        var settlement = Settlement.Create(100, 200, 400_000, 300, "Bank", "IR00300", "Seller");
        settlement.MarkProcessing();
        settlement.PutOnHold();
        var balance = SellerBalance.Create(400, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        SettlementReconciliationAudit? audit = null;
        lifecycle.Setup(x => x.AddSettlementReconciliationAudit(It.IsAny<SettlementReconciliationAudit>()))
            .Callback<SettlementReconciliationAudit>(value => audit = value);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var ids = new Mock<IIdGenerator>();
        long nextId = 500;
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => ++nextId);

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object, Mock.Of<ISellerPayoutGateway>(), Mock.Of<ISellerManagementRepository>());

        var result = await service.ReconcileAsync(settlement.Id, 777, true, " BANK-100 ", "  Verified against bank statement  ");

        Assert.Equal("Completed", result.Status);
        Assert.Equal("BANK-100", settlement.Reference);
        Assert.Equal(600_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.NotNull(audit);
        Assert.Equal(777, audit!.AdminUserId);
        Assert.Equal(settlement.Id, audit.SettlementId);
        Assert.True(audit.TransferCompleted);
        Assert.Equal("BANK-100", audit.BankReference);
        Assert.Equal("Verified against bank statement", audit.Note);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IIdGenerator CreateIdGenerator()
    {
        long next = 9000;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref next));
        return ids.Object;
    }

}
