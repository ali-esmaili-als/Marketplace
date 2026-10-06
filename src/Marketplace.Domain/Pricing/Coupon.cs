using Marketplace.Domain.Common;

namespace Marketplace.Domain.Pricing;

public sealed class Coupon : AggregateRoot<long>
{
    private Coupon() { }
    public long SellerId { get; private set; }
    public long StoreId { get; private set; }
    public string Code { get; private set; } = null!;
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public long? MaxDiscountAmountIRR { get; private set; }
    public long? MinimumPurchaseIRR { get; private set; }
    public int? MaxUses { get; private set; }
    public bool NewCustomerOnly { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? EndsAtUtc { get; private set; }

    public static Coupon Create(long id,long sellerId,long storeId,string code,DiscountType type,decimal value,long? maxDiscountAmountIrr=null,long? minimumPurchaseIrr=null,int? maxUses=null,bool newCustomerOnly=false,DateTime? startsAtUtc=null,DateTime? endsAtUtc=null)
    {
        if(id<=0||sellerId<=0||storeId<=0||string.IsNullOrWhiteSpace(code)||value<0||(type==DiscountType.Percentage&&value>100)||maxDiscountAmountIrr<0||minimumPurchaseIrr<0||maxUses<=0||(startsAtUtc.HasValue&&endsAtUtc.HasValue&&endsAtUtc<=startsAtUtc))
            throw new DomainException("Invalid coupon.");
        return new Coupon { Id=id,SellerId=sellerId,StoreId=storeId,Code=code.Trim().ToUpperInvariant(),DiscountType=type,DiscountValue=value,MaxDiscountAmountIRR=maxDiscountAmountIrr,MinimumPurchaseIRR=minimumPurchaseIrr,MaxUses=maxUses,NewCustomerOnly=newCustomerOnly,IsActive=true,StartsAtUtc=startsAtUtc,EndsAtUtc=endsAtUtc };
    }
    public bool IsRunning(DateTime nowUtc) => IsActive && (!StartsAtUtc.HasValue||nowUtc>=StartsAtUtc.Value) && (!EndsAtUtc.HasValue||nowUtc<EndsAtUtc.Value);
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}