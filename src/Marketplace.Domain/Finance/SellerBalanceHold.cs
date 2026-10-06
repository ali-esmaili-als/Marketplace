using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public enum BalanceHoldStatus : byte { Active=1, Released=2, Consumed=3 }

public sealed class SellerBalanceHold : Entity<long>
{
    private SellerBalanceHold() { }
    public long SellerId { get; private set; }
    public long? OrderId { get; private set; }
    public long AmountIRR { get; private set; }
    public string Reason { get; private set; } = null!;
    public BalanceHoldStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static SellerBalanceHold Create(long id,long sellerId,long? orderId,long amountIrr,string reason)
    {
        if(amountIrr<=0 || string.IsNullOrWhiteSpace(reason)) throw new DomainException("Invalid balance hold.");
        return new SellerBalanceHold { Id=id, SellerId=sellerId, OrderId=orderId, AmountIRR=amountIrr, Reason=reason.Trim(), Status=BalanceHoldStatus.Active, CreatedAtUtc=DateTime.UtcNow };
    }
    public void Release(){RequireActive();Status=BalanceHoldStatus.Released;CompletedAtUtc=DateTime.UtcNow;}
    public void Consume(){RequireActive();Status=BalanceHoldStatus.Consumed;CompletedAtUtc=DateTime.UtcNow;}
    private void RequireActive(){if(Status!=BalanceHoldStatus.Active)throw new DomainException("Balance hold is not active.");}
}
