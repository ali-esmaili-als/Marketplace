using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementReconciliationAuditTests
{
    [Fact]
    public void CompletedTransfer_RequiresBankReferenceAndTrimsAuditFields()
    {
        var audit = SettlementReconciliationAudit.Create(1, 2, 3, true, " BANK-REF ", "  Confirmed with bank  ");

        Assert.Equal("BANK-REF", audit.BankReference);
        Assert.Equal("Confirmed with bank", audit.Note);
        Assert.True(audit.TransferCompleted);
        Assert.Equal(3, audit.AdminUserId);
    }

    [Fact]
    public void CompletedTransferWithoutReferenceIsRejected()
    {
        Assert.Throws<DomainException>(() =>
            SettlementReconciliationAudit.Create(1, 2, 3, true, " ", "Confirmed with bank"));
    }

    [Fact]
    public void BlankNoteIsRejectedForAnyReconciliation()
    {
        Assert.Throws<DomainException>(() =>
            SettlementReconciliationAudit.Create(1, 2, 3, false, null, "  "));
    }

    [Fact]
    public void OversizedNoteAndBankReferenceAreRejected()
    {
        Assert.Throws<DomainException>(() =>
            SettlementReconciliationAudit.Create(1, 2, 3, false, null, new string('x', 2001)));
        Assert.Throws<DomainException>(() =>
            SettlementReconciliationAudit.Create(1, 2, 3, true, new string('x', 201), "Confirmed"));
    }
}
