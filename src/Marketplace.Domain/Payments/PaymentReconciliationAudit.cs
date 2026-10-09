using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public sealed class PaymentReconciliationAudit : AggregateRoot<long>
{
    private PaymentReconciliationAudit() { }

    public long PaymentId { get; private set; }
    public long AdminUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Note { get; private set; } = string.Empty;
    public string? BankReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static PaymentReconciliationAudit Create(long paymentId, long adminUserId, string action, string note, string? bankReference)
    {
        if (paymentId <= 0 || adminUserId <= 0) throw new DomainException("Invalid reconciliation audit identity.");
        if (action is not ("RefundCompleted" or "KeepOpen")) throw new DomainException("Invalid reconciliation action.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 2000) throw new DomainException("A note of at most 2000 characters is required.");
        if (bankReference?.Trim().Length > 200) throw new DomainException("Bank reference cannot exceed 200 characters.");
        if (action == "RefundCompleted" && string.IsNullOrWhiteSpace(bankReference)) throw new DomainException("Bank reference is required for a completed refund.");
        return new PaymentReconciliationAudit
        {
            PaymentId = paymentId,
            AdminUserId = adminUserId,
            Action = action,
            Note = note.Trim(),
            BankReference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
