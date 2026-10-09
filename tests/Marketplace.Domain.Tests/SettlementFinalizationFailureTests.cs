using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Finance;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementFinalizationFailureTests
{
    [Fact]
    public async Task Database_finalization_failure_after_bank_response_puts_settlement_on_hold_and_keeps_reservation()
    {
        var settlement = Settlement.Create(501, 502, 400_000, 503, "Bank", "IR00503", "Seller");
        var balance = SellerBalance.Create(504, settlement.SellerId);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(settlement.AmountIRR);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSettlementAsync(settlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) =>
                Task.FromException<SettlementResult>(new InvalidOperationException("Database commit failed.")));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var payout = new Mock<ISellerPayoutGateway>();
        payout.Setup(x => x.TransferAsync(
                settlement.BankNameSnapshot, settlement.IbanSnapshot, settlement.AccountHolderNameSnapshot,
                settlement.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "BANK-REF-501", (string?)null));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, CreateIdGenerator(), payout.Object, Mock.Of<ISellerManagementRepository>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(settlement.Id));

        Assert.Equal("Database commit failed.", error.Message);
        Assert.Equal(SettlementStatus.OnHold, settlement.Status);
        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(x => x.ExecuteInSerializableTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<int>>>(), CancellationToken.None), Times.Exactly(2));
    }
    private static IIdGenerator CreateIdGenerator()
    {
        long next = 12000;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref next));
        return ids.Object;
    }

}
