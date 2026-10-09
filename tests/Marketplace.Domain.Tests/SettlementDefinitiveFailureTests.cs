using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Finance;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementDefinitiveFailureTests
{
    [Fact]
    public async Task DefinitiveGatewayRejection_releases_reserved_funds_and_records_failure_once()
    {
        var settlement = Settlement.Create(501, 601, 275_000, 701, "Test Bank", "IR00701", "Seller");
        var balance = SellerBalance.Create(801, settlement.SellerId);
        balance.AddAvailable(900_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(settlement.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        BalanceTransaction? failureEntry = null;
        lifecycle.Setup(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()))
            .Callback<BalanceTransaction>(entry => failureEntry = entry);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var ids = new Mock<IIdGenerator>();
        long nextId = 900;
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (string?)null, "Rejected by bank: invalid destination account"));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object, payout.Object, Mock.Of<ISellerManagementRepository>());

        var result = await service.ProcessAsync(settlement.Id);

        Assert.Equal("Failed", result.Status);
        Assert.Equal(SettlementStatus.Failed, settlement.Status);
        Assert.Equal(900_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(900_000, balance.WithdrawableIRR);

        Assert.NotNull(failureEntry);
        Assert.Equal(BalanceTransactionType.SettlementFailed, failureEntry!.Type);
        Assert.Equal(settlement.Id, failureEntry.SettlementId);
        Assert.Equal(275_000, failureEntry.AmountIRR);
        Assert.Equal(275_000, failureEntry.BalanceBeforeIRR);
        Assert.Equal(0, failureEntry.BalanceAfterIRR);
        Assert.Equal(BalanceBucket.ReservedForSettlement, failureEntry.Bucket);
        Assert.Contains("Rejected by bank", failureEntry.Reference);

        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
