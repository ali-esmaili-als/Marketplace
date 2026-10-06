using Marketplace.Application.Abstractions;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Pricing;

namespace Marketplace.Application.Pricing;

public sealed record PricingLine(CartItem Item,CheckoutLineData Data,long BaseUnitIRR,long WarrantyIRR,long BaseLineIRR,long CampaignDiscountIRR,long CouponDiscountIRR,long FinalLineIRR,long EffectiveUnitIRR);
public sealed record PricingResult(IReadOnlyList<PricingLine> Lines,long SubtotalIRR,long CampaignDiscountIRR,long CouponDiscountIRR,long TotalIRR,Coupon? Coupon);

public sealed class PricingService
{
    private readonly IPricingRepository _pricing;
    public PricingService(IPricingRepository pricing)=>_pricing=pricing;

    public async Task<PricingResult> PriceAsync(long customerId,long storeId,long sellerId,IReadOnlyList<(CartItem Item,CheckoutLineData Data)> input,string? couponCode,DateTime nowUtc,CancellationToken ct=default)
    {
        if(input.Count==0) throw new DomainException("Cart is empty.");
        var lines=new List<PricingLine>(input.Count);
        Campaign? firstCampaign=null;
        foreach(var x in input)
        {
            var baseUnit=x.Data.Variant.PriceIRR??x.Data.Product.BasePriceIRR;
            var warranty=x.Data.Warranty?.PriceIRR??0;
            var baseLine=checked((baseUnit+warranty)*x.Item.Quantity);
            var campaign=await _pricing.GetRunningCampaignAsync(storeId,x.Data.Product.Id,x.Data.Variant.Id,nowUtc,ct);
            var campaignDiscount=0L;
            if(campaign is not null)
            {
                if(campaign.DiscountType==DiscountType.Percentage)
                    campaignDiscount=checked((long)Math.Floor(baseLine*campaign.DiscountValue/100m));
                else campaignDiscount=Math.Min(baseLine,checked((long)campaign.DiscountValue));
                if(campaignDiscount>0) firstCampaign ??=campaign;
            }
            var effective=baseLine-campaignDiscount;
            lines.Add(new PricingLine(x.Item,x.Data,baseUnit,warranty,baseLine,campaignDiscount,0,effective,Math.Max(0,effective/x.Item.Quantity-warranty)));
        }
        if(!string.IsNullOrWhiteSpace(couponCode))
        {
            if(firstCampaign is not null) throw new DomainException("Coupon cannot be combined with a campaign.");
            var coupon=await _pricing.GetCouponAsync(storeId,couponCode.Trim(),ct)??throw new DomainException("Coupon not found.");
            if(!coupon.IsRunning(nowUtc)) throw new DomainException("Coupon is not active.");
            var subtotal=lines.Sum(x=>x.BaseLineIRR);
            if(coupon.MinimumPurchaseIRR.HasValue&&subtotal<coupon.MinimumPurchaseIRR.Value) throw new DomainException("Minimum purchase for this coupon is not met.");
            if(coupon.NewCustomerOnly&&!await _pricing.IsNewCustomerAsync(customerId,ct)) throw new DomainException("Coupon is only for new customers.");
            if(coupon.MaxUses.HasValue&&await _pricing.GetCouponUsageCountAsync(coupon.Id,ct)>=coupon.MaxUses.Value) throw new DomainException("Coupon usage limit has been reached.");
            if(await _pricing.HasCustomerUsedCouponAsync(coupon.Id,customerId,ct)) throw new DomainException("Customer has already used this coupon.");
            var hasScope=await _pricing.CouponHasAnyScopeAsync(coupon.Id,ct);
            var eligible=0L;
            foreach(var l in lines)
            {
                var ok=!hasScope;
                if(hasScope)
                    ok=await _pricing.CouponHasProductScopeAsync(coupon.Id,l.Data.Product.Id,ct)
                       || await _pricing.CouponHasCategoryScopeAsync(coupon.Id,l.Data.Product.CategoryId,ct);
                if(ok) eligible=checked(eligible+l.BaseLineIRR);
            }
            if(eligible<=0) throw new DomainException("Coupon does not apply to cart items.");
            var discount=coupon.DiscountType==DiscountType.Percentage
                ? checked((long)Math.Floor(eligible*coupon.DiscountValue/100m))
                : Math.Min(eligible,checked((long)coupon.DiscountValue));
            if(coupon.MaxDiscountAmountIRR.HasValue) discount=Math.Min(discount,coupon.MaxDiscountAmountIRR.Value);
            discount=Math.Min(discount,eligible);
            var remaining=discount;
            for(var i=0;i<lines.Count&&remaining>0;i++)
            {
                var l=lines[i];
                var ok=!hasScope || await _pricing.CouponHasProductScopeAsync(coupon.Id,l.Data.Product.Id,ct) || await _pricing.CouponHasCategoryScopeAsync(coupon.Id,l.Data.Product.CategoryId,ct);
                if(!ok) continue;
                var d=Math.Min(l.BaseLineIRR,remaining);
                lines[i]=l with { CouponDiscountIRR=d,FinalLineIRR=l.FinalLineIRR-d,EffectiveUnitIRR=Math.Max(0,(l.FinalLineIRR-d)/l.Item.Quantity-l.WarrantyIRR) };
                remaining-=d;
            }
            var total=checked(lines.Sum(x=>x.FinalLineIRR));
            return new PricingResult(lines,subtotal,0,discount,total,coupon);
        }
        return new PricingResult(lines,lines.Sum(x=>x.BaseLineIRR),lines.Sum(x=>x.CampaignDiscountIRR),0,lines.Sum(x=>x.FinalLineIRR),null);
    }
}