using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public sealed class RefundReconciliationAudit : AggregateRoot<long>
{
    private RefundReconciliationAudit() { }

    public long RefundId { get; private set; }
    public long AdminUserId { get; private set; }
    public bool TransferCompleted { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public string? BankReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static RefundReconciliationAudit Create(long refundId, long adminUserId, bool transferCompleted, string note, string? bankReference)
    {
        if (refundId <= 0 || adminUserId <= 0) throw new DomainException("Invalid refund reconciliation identity.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 2000) throw new DomainException("A note of at most 2000 characters is required.");
        if (bankReference?.Trim().Length > 200) throw new DomainException("Bank reference cannot exceed 200 characters.");
        if (transferCompleted && string.IsNullOrWhiteSpace(bankReference)) throw new DomainException("Bank reference is required for a completed refund.");
        return new RefundReconciliationAudit
        {
            RefundId = refundId,
            AdminUserId = adminUserId,
            TransferCompleted = transferCompleted,
            Note = note.Trim(),
            BankReference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}