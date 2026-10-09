using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class SettlementReconciliationAudit : Entity<long>
{
    private SettlementReconciliationAudit() { }

    public long SettlementId { get; private set; }
    public long AdminUserId { get; private set; }
    public bool TransferCompleted { get; private set; }
    public string Note { get; private set; } = null!;
    public string? BankReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SettlementReconciliationAudit Create(
        long id, long settlementId, long adminUserId, bool transferCompleted,
        string? bankReference, string note)
    {
        if (id <= 0 || settlementId <= 0 || adminUserId <= 0)
            throw new DomainException("Settlement reconciliation identifiers must be positive.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 2000)
            throw new DomainException("A reconciliation note between 1 and 2000 characters is required.");
        var reference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
        if (reference?.Length > 200)
            throw new DomainException("Bank reference cannot exceed 200 characters.");
        if (transferCompleted && reference is null)
            throw new DomainException("Bank reference is required when confirming a completed transfer.");

        return new SettlementReconciliationAudit
        {
            Id = id,
            SettlementId = settlementId,
            AdminUserId = adminUserId,
            TransferCompleted = transferCompleted,
            Note = note.Trim(),
            BankReference = reference,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
