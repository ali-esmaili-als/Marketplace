using System;
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

    [Fact]
    public void CompleteSettlement_CannotConsumeMoreThanReserved()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        Assert.Throws<DomainException>(() => balance.CompleteSettlement(400_001));

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
        Assert.Equal(600_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void FailSettlement_CannotReleaseMoreThanReserved()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(1_000_000);
        balance.ReserveForSettlement(400_000);

        Assert.Throws<DomainException>(() => balance.FailSettlement(400_001));

        Assert.Equal(1_000_000, balance.AvailableIRR);
        Assert.Equal(400_000, balance.ReservedForSettlementIRR);
    }

    [Fact]
    public void BalanceOverflow_DoesNotPartiallyChangeBucket()
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(long.MaxValue);

        Assert.Throws<OverflowException>(() => balance.AddAvailable(1));

        Assert.Equal(long.MaxValue, balance.AvailableIRR);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SettlementBucketMutations_RejectNonPositiveAmounts(int amount)
    {
        var balance = SellerBalance.Create(1, 2);
        balance.AddAvailable(100);

        Assert.Throws<DomainException>(() => balance.ReserveForSettlement(amount));
        Assert.Throws<DomainException>(() => balance.CompleteSettlement(amount));
        Assert.Throws<DomainException>(() => balance.FailSettlement(amount));

        Assert.Equal(100, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
    }

}
