using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class Settlement : AggregateRoot<long>
{
    private Settlement() { }

    public long SellerId { get; private set; }
    public long AmountIRR { get; private set; }
    public SettlementStatus Status { get; private set; }
    public long BankAccountId { get; private set; }
    public string BankNameSnapshot { get; private set; } = null!;
    public string IbanSnapshot { get; private set; } = null!;
    public string AccountHolderNameSnapshot { get; private set; } = null!;
    public string? Reference { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static Settlement Create(
        long id,
        long sellerId,
        long amount,
        long bankId,
        string bank,
        string iban,
        string holder)
    {
        if (amount <= 0 ||
            string.IsNullOrWhiteSpace(iban) ||
            string.IsNullOrWhiteSpace(holder))
            throw new DomainException("Invalid settlement.");

        return new()
        {
            Id = id,
            SellerId = sellerId,
            AmountIRR = amount,
            BankAccountId = bankId,
            BankNameSnapshot = bank,
            IbanSnapshot = iban,
            AccountHolderNameSnapshot = holder,
            Status = SettlementStatus.Requested,
            RequestedAtUtc = DateTime.UtcNow
        };
    }

    public void MarkProcessing()
    {
        Require(SettlementStatus.Requested);
        Status = SettlementStatus.Processing;
    }

    public void Complete(string reference)
    {
        if (Status is not SettlementStatus.Processing)
            throw new DomainException("Settlement is not processing.");

        if (string.IsNullOrWhiteSpace(reference))
            throw new DomainException("Settlement reference is required.");

        Status = SettlementStatus.Completed;
        Reference = reference.Trim();
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        if (Status is not (SettlementStatus.Processing or SettlementStatus.OnHold))
            throw new DomainException("Only processing or on-hold settlements can fail.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Settlement failure reason is required.");

        Status = SettlementStatus.Failed;
        FailureReason = reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (SettlementStatus.Requested or SettlementStatus.OnHold))
            throw new DomainException("Only requested or on-hold settlements can be cancelled.");

        Status = SettlementStatus.Cancelled;
    }

    public void PutOnHold()
    {
        Require(SettlementStatus.Processing);
        Status = SettlementStatus.OnHold;
    }

    private void Require(SettlementStatus status)
    {
        if (Status != status)
            throw new DomainException("Invalid settlement state.");
    }
}
