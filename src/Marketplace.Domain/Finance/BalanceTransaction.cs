using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class BalanceTransaction : Entity<long>
{
    private BalanceTransaction() { }
    public long SellerId { get; private set; }
    public long? OrderId { get; private set; }
    public long? SettlementId { get; private set; }
    public long? RefundId { get; private set; }
    public BalanceTransactionType Type { get; private set; }
    public BalanceBucket Bucket { get; private set; }
    public long AmountIRR { get; private set; }
    public long BalanceBeforeIRR { get; private set; }
    public long BalanceAfterIRR { get; private set; }
    public string? Reference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static BalanceTransaction Create(long id,long sellerId,long? orderId,long? settlementId,BalanceTransactionType type,long amountIrr,long beforeIrr,long afterIrr,string? reference,BalanceBucket bucket=BalanceBucket.Available,long? refundId=null)
    {
        if(amountIrr<0 || beforeIrr<0 || afterIrr<0) throw new DomainException("Balance transaction amounts cannot be negative.");
        return new BalanceTransaction { Id=id, SellerId=sellerId, OrderId=orderId, SettlementId=settlementId, RefundId=refundId, Type=type, Bucket=bucket, AmountIRR=amountIrr, BalanceBeforeIRR=beforeIrr, BalanceAfterIRR=afterIrr, Reference=reference?.Trim(), CreatedAtUtc=DateTime.UtcNow };
    }
}
