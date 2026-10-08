using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementTests
{
    private static Settlement CreateSettlement() =>
        Settlement.Create(1, 10, 250_000, 7, "Test Bank", "IR123456789", "Seller Name");

    [Fact]
    public void MarkProcessing_OnlyAllowsRequestedSettlement()
    {
        var settlement = CreateSettlement();

        settlement.MarkProcessing();

        Assert.Equal(SettlementStatus.Processing, settlement.Status);
        Assert.Throws<DomainException>(() => settlement.MarkProcessing());
    }

    [Fact]
    public void CompletedSettlement_CannotBeFailedOrCancelled()
    {
        var settlement = CreateSettlement();
        settlement.MarkProcessing();
        settlement.Complete("BANK-REF-1");

        Assert.Equal(SettlementStatus.Completed, settlement.Status);
        Assert.Equal("BANK-REF-1", settlement.Reference);
        Assert.NotNull(settlement.CompletedAtUtc);
        Assert.Throws<DomainException>(() => settlement.Fail("late failure"));
        Assert.Throws<DomainException>(() => settlement.Cancel());
    }

    [Fact]
    public void FailedSettlement_CannotBeReprocessed()
    {
        var settlement = CreateSettlement();
        settlement.MarkProcessing();
        settlement.Fail("Bank rejected transfer.");

        Assert.Equal(SettlementStatus.Failed, settlement.Status);
        Assert.Throws<DomainException>(() => settlement.MarkProcessing());
    }

    [Fact]
    public void SellerBalance_ReservesWithdrawableAmountAndReleasesOnFailure()
    {
        var balance = SellerBalance.Create(3, 10);
        balance.AddAvailable(500_000);

        balance.ReserveForSettlement(250_000);

        Assert.Equal(250_000, balance.WithdrawableIRR);
        Assert.Equal(250_000, balance.ReservedForSettlementIRR);

        balance.FailSettlement(250_000);

        Assert.Equal(500_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        Assert.Equal(500_000, balance.WithdrawableIRR);
    }

    [Fact]
    public void SellerBalance_CannotReserveMoreThanWithdrawableFunds()
    {
        var balance = SellerBalance.Create(3, 10);
        balance.AddAvailable(100_000);
        balance.ReserveForSettlement(80_000);

        Assert.Throws<DomainException>(() => balance.ReserveForSettlement(30_000));
        Assert.Equal(20_000, balance.WithdrawableIRR);
    }
}
