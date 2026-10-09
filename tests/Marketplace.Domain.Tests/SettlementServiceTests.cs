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
            lifecycle.Object, uow.Object, Mock.Of<IIdGenerator>(), payout.Object, Mock.Of<ISellerManagementRepository>());

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
            lifecycle.Object, uow.Object, Mock.Of<IIdGenerator>(), payout.Object, Mock.Of<ISellerManagementRepository>());

        await Assert.ThrowsAsync<TimeoutException>(() => service.ProcessAsync(settlement.Id));

        Assert.Equal(SettlementStatus.OnHold, settlement.Status);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
        payout.Verify(x => x.TransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
