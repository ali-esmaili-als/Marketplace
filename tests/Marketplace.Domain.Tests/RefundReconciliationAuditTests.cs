using Marketplace.Domain.Common;
using Marketplace.Domain.Refunds;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class RefundReconciliationAuditTests
{
    [Fact]
    public void Create_CompletedTransfer_TrimsAndRecordsEvidence()
    {
        var audit = RefundReconciliationAudit.Create(12, 34, true, "  verified in bank portal  ", " BANK-77 ");

        Assert.Equal(12, audit.RefundId);
        Assert.Equal(34, audit.AdminUserId);
        Assert.True(audit.TransferCompleted);
        Assert.Equal("verified in bank portal", audit.Note);
        Assert.Equal("BANK-77", audit.BankReference);
        Assert.NotEqual(default, audit.CreatedAtUtc);
    }

    [Fact]
    public void Create_CompletedTransfer_RequiresBankReference()
    {
        Assert.Throws<DomainException>(() =>
            RefundReconciliationAudit.Create(12, 34, true, "verified", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RequiresNote(string note)
    {
        Assert.Throws<DomainException>(() =>
            RefundReconciliationAudit.Create(12, 34, false, note, null));
    }

    [Fact]
    public void Create_RejectsOversizedNote()
    {
        Assert.Throws<DomainException>(() =>
            RefundReconciliationAudit.Create(12, 34, false, new string('x', 2001), null));
    }
}