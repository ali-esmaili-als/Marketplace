using Marketplace.Domain.Finance;

namespace Marketplace.Application.Tests.Finance;

public sealed class SellerBalanceTests
{
    [Fact]
    public void ReserveForSettlement_DoesNotReduceAvailable_ButReducesWithdrawable()
    {
        var balance = SellerBalance.Create(1, 10);
        balance.AddAvailable(1_000_000);

        balance.ReserveForSettlement(400_000);

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void CompleteSettlement_ReducesReservedAndAvailable()
    {
        var balance = SellerBalance.Create(1, 10);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        balance.CompleteSettlement(400_000);

        Assert.Equal(600_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void FailedSettlement_ReleasesReservation_WithoutChangingAvailable()
    {
        var balance = SellerBalance.Create(1, 10);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        balance.FailSettlement(400_000);

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1_000_000, balance.WithdrawableIRR);
    }
}
