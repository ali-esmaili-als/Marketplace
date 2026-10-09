using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SellerBalanceTests
{
    [Fact]
    public void FailedSettlement_ReleasesReservationWithoutCreditingAvailableTwice()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        balance.FailSettlement(400_000);

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1_000_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void SettlementCannotReserveMoreThanWithdrawableBalance()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(700_000);

        Assert.Throws<DomainException>(() => balance.ReserveForSettlement(300_001));
        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(700_000, balance.ReservedForSettlementIRR);
        Assert.Equal(300_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void CompletingSettlementConsumesOnlyReservedAmount()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        balance.CompleteSettlement(400_000);

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(1_000_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void RepayLiabilityCannotExceedLiability()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddLiability(50_000);

        Assert.Throws<DomainException>(() => balance.RepayLiability(50_001));
        Assert.Equal(50_000, balance.LiabilityIRR);
    }
}
