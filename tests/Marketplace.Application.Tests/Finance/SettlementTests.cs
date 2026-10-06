using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;

namespace Marketplace.Application.Tests.Finance;

public sealed class SettlementTests
{
    [Fact]
    public void Requested_CanMoveToProcessing()
    {
        var settlement = Create();

        settlement.MarkProcessing();

        Assert.Equal(SettlementStatus.Processing, settlement.Status);
    }

    [Fact]
    public void Processing_CanComplete_WithGatewayReference()
    {
        var settlement = Create();
        settlement.MarkProcessing();

        settlement.Complete("BANK-123");

        Assert.Equal(SettlementStatus.Completed, settlement.Status);
        Assert.Equal("BANK-123", settlement.Reference);
        Assert.NotNull(settlement.CompletedAtUtc);
    }

    [Fact]
    public void Processing_CanFail_AndRequiresReason()
    {
        var settlement = Create();
        settlement.MarkProcessing();

        settlement.Fail("Bank rejected transfer.");

        Assert.Equal(SettlementStatus.Failed, settlement.Status);
        Assert.Equal("Bank rejected transfer.", settlement.FailureReason);
    }

    [Fact]
    public void Requested_CanCancel()
    {
        var settlement = Create();

        settlement.Cancel();

        Assert.Equal(SettlementStatus.Cancelled, settlement.Status);
    }

    [Fact]
    public void Completed_CannotBeCancelled()
    {
        var settlement = Create();
        settlement.MarkProcessing();
        settlement.Complete("BANK-123");

        Assert.Throws<DomainException>(() => settlement.Cancel());
    }

    [Fact]
    public void Failed_CannotBeCompleted()
    {
        var settlement = Create();
        settlement.MarkProcessing();
        settlement.Fail("Rejected.");

        Assert.Throws<DomainException>(() => settlement.Complete("BANK-123"));
    }

    [Fact]
    public void Complete_RequiresReference()
    {
        var settlement = Create();
        settlement.MarkProcessing();

        Assert.Throws<DomainException>(() => settlement.Complete(" "));
    }

    private static Settlement Create()
        => Settlement.Create(
            id: 1,
            sellerId: 10,
            amount: 1_000_000,
            bankId: 20,
            bank: "Test Bank",
            iban: "IR123",
            holder: "Seller");
}
