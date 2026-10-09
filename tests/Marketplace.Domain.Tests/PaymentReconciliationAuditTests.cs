using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentReconciliationAuditTests
{
    [Fact]
    public void Create_KeepOpenAudit_TrimsAndPersistsReviewDetails()
    {
        var audit = PaymentReconciliationAudit.Create(12, 34, "KeepOpen", "  waiting for bank confirmation  ", " REF-9 ");

        Assert.Equal(12, audit.PaymentId);
        Assert.Equal(34, audit.AdminUserId);
        Assert.Equal("KeepOpen", audit.Action);
        Assert.Equal("waiting for bank confirmation", audit.Note);
        Assert.Equal("REF-9", audit.BankReference);
        Assert.NotEqual(default, audit.CreatedAtUtc);
    }

    [Fact]
    public void Create_CompletedRefund_RequiresBankReference()
    {
        Assert.Throws<DomainException>(() =>
            PaymentReconciliationAudit.Create(12, 34, "RefundCompleted", "Bank transfer verified", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RequiresReviewNote(string note)
    {
        Assert.Throws<DomainException>(() =>
            PaymentReconciliationAudit.Create(12, 34, "KeepOpen", note, null));
    }

    [Fact]
    public void Create_RejectsUnsupportedAction()
    {
        Assert.Throws<DomainException>(() =>
            PaymentReconciliationAudit.Create(12, 34, "MarkPaid", "manual review", null));
    }
}
