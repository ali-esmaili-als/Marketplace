using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class Settlement : AggregateRoot<long>
{
    private Settlement() { }
    public long SellerId { get; private set; }
    public string? RequestKey { get; private set; }
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

    public static Settlement Create(long id,long sellerId,long amountIrr,long bankAccountId,string bankName,string iban,string holder)
        => Create(id,sellerId,amountIrr,bankAccountId,bankName,iban,holder,null);

    public static Settlement Create(long id,long sellerId,long amountIrr,long bankAccountId,string bankName,string iban,string holder,string? requestKey)
    {
        if(id<=0 || sellerId<=0 || amountIrr<=0 || bankAccountId<=0 || string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(iban) || string.IsNullOrWhiteSpace(holder)) throw new DomainException("Invalid settlement data.");
        if(requestKey is not null && (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length>64)) throw new DomainException("Invalid settlement request key.");
        return new Settlement { Id=id, SellerId=sellerId, RequestKey=string.IsNullOrWhiteSpace(requestKey)?null:requestKey.Trim(), AmountIRR=amountIrr, Status=SettlementStatus.Requested, BankAccountId=bankAccountId, BankNameSnapshot=bankName.Trim(), IbanSnapshot=iban.Trim(), AccountHolderNameSnapshot=holder.Trim(), RequestedAtUtc=DateTime.UtcNow };
    }
    public void MarkProcessing(){Require(SettlementStatus.Requested);Status=SettlementStatus.Processing;}
    public void Complete(string? reference){if(Status is not (SettlementStatus.Requested or SettlementStatus.Processing or SettlementStatus.OnHold))throw new DomainException("Settlement cannot be completed.");Status=SettlementStatus.Completed;Reference=reference?.Trim();CompletedAtUtc=DateTime.UtcNow;}
    public void Fail(string reason){if(Status is SettlementStatus.Completed or SettlementStatus.Cancelled)throw new DomainException("Settlement cannot fail.");if(string.IsNullOrWhiteSpace(reason))throw new DomainException("Failure reason is required.");Status=SettlementStatus.Failed;FailureReason=reason.Trim();CompletedAtUtc=DateTime.UtcNow;}
    public void Cancel(){if(Status is SettlementStatus.Completed or SettlementStatus.Processing)throw new DomainException("Settlement cannot be cancelled.");Status=SettlementStatus.Cancelled;CompletedAtUtc=DateTime.UtcNow;}
    public void PutOnHold(){if(Status is SettlementStatus.Completed or SettlementStatus.Failed or SettlementStatus.Cancelled)throw new DomainException("Settlement cannot be put on hold.");Status=SettlementStatus.OnHold;}
    private void Require(SettlementStatus status){if(Status!=status)throw new DomainException($"Settlement must be in {status} status.");}
}
