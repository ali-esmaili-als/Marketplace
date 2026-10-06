using Marketplace.Domain.Common;

namespace Marketplace.Domain.Coupons;

public sealed class Coupon : AggregateRoot<long>
{
    private Coupon() { }
    public long StoreId { get; private set; }
    public string Code { get; private set; } = null!;
    public CouponDiscountType DiscountType { get; private set; }
    public long DiscountValue { get; private set; }
    public long? MaximumEligibleAmountIRR { get; private set; }
    public long? MinimumCartAmountIRR { get; private set; }
    public bool NewCustomerOnly { get; private set; }
    public int? MaxUsageCount { get; private set; }
    public int? MaxUsagePerCustomer { get; private set; }
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public bool AllowCampaignCombination { get; private set; }
    public int ReservedUsageCount { get; private set; }
    public int UsedUsageCount { get; private set; }

    public static Coupon Create(long id,long storeId,string code,CouponDiscountType discountType,long discountValue,long? maximumEligibleAmountIrr,long? minimumCartAmountIrr,bool newCustomerOnly,int? maxUsageCount,int? maxUsagePerCustomer,DateTime? startsAtUtc,DateTime? expiresAtUtc,bool allowCampaignCombination)
    {
        if(string.IsNullOrWhiteSpace(code)) throw new DomainException("Coupon code is required.");
        if(discountValue<=0 || maximumEligibleAmountIrr is <=0 || minimumCartAmountIrr is <0 || maxUsageCount is <=0 || maxUsagePerCustomer is <=0) throw new DomainException("Invalid coupon limits or discount.");
        if(discountType==CouponDiscountType.Percent && discountValue>100) throw new DomainException("Percentage coupon cannot exceed 100.");
        if(startsAtUtc.HasValue && expiresAtUtc.HasValue && expiresAtUtc<=startsAtUtc) throw new DomainException("Coupon expiry must be after start.");
        return new Coupon { Id=id, StoreId=storeId, Code=code.Trim().ToUpperInvariant(), DiscountType=discountType, DiscountValue=discountValue, MaximumEligibleAmountIRR=maximumEligibleAmountIrr, MinimumCartAmountIRR=minimumCartAmountIrr, NewCustomerOnly=newCustomerOnly, MaxUsageCount=maxUsageCount, MaxUsagePerCustomer=maxUsagePerCustomer, StartsAtUtc=startsAtUtc, ExpiresAtUtc=expiresAtUtc, IsActive=true, AllowCampaignCombination=allowCampaignCombination };
    }
    public long CalculateDiscount(long eligibleAmount)
    {
        if(eligibleAmount<0) throw new DomainException("Eligible amount cannot be negative.");
        var capped=MaximumEligibleAmountIRR.HasValue ? Math.Min(eligibleAmount,MaximumEligibleAmountIRR.Value) : eligibleAmount;
        return DiscountType switch { CouponDiscountType.Percent => Math.Min(eligibleAmount,capped*DiscountValue/100), CouponDiscountType.Fixed => Math.Min(eligibleAmount,DiscountValue), _ => throw new DomainException("Invalid coupon discount type.") };
    }
    public void ReserveUsage(){if(MaxUsageCount.HasValue && ReservedUsageCount+UsedUsageCount>=MaxUsageCount.Value)throw new DomainException("Coupon usage limit reached.");ReservedUsageCount++;}
    public void MarkUsageUsed(){if(ReservedUsageCount<=0)throw new DomainException("No reserved coupon usage exists.");ReservedUsageCount--;UsedUsageCount++;}
    public void ReleaseUsage(){if(ReservedUsageCount<=0)throw new DomainException("No reserved coupon usage exists.");ReservedUsageCount--;}
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
