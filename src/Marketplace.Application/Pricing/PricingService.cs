using Marketplace.Application.Abstractions;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Pricing;

namespace Marketplace.Application.Pricing;

public sealed record PricingLine(CartItem Item,CheckoutLineData Data,long BaseUnitIRR,long WarrantyIRR,long BaseLineIRR,long CampaignDiscountIRR,long CouponDiscountIRR,long FinalLineIRR,long EffectiveUnitIRR,Campaign? Campaign);
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
            var merchandiseLine=checked(baseUnit*x.Item.Quantity);
            var campaign=await _pricing.GetRunningCampaignAsync(storeId,x.Data.Product.Id,x.Data.Variant.Id,nowUtc,ct);
            var campaignDiscount=0L;
            if(campaign is not null)
            {
                campaignDiscount=campaign.DiscountType==DiscountType.Percentage
                    ? checked((long)Math.Floor(merchandiseLine*campaign.DiscountValue/100m))
                    : Math.Min(merchandiseLine,checked((long)campaign.DiscountValue));
                firstCampaign ??= campaign;
            }
            var finalMerchandise=merchandiseLine-campaignDiscount;
            var finalLine=checked(finalMerchandise+checked(warranty*x.Item.Quantity));
            lines.Add(new PricingLine(x.Item,x.Data,baseUnit,warranty,merchandiseLine,campaignDiscount,0,finalLine,finalMerchandise/x.Item.Quantity,campaign));
        }
        if(!string.IsNullOrWhiteSpace(couponCode))
        {
            if(firstCampaign is not null) throw new DomainException("Coupon cannot be combined with a campaign.");
            var coupon=await _pricing.GetCouponAsync(storeId,couponCode.Trim(),ct)??throw new DomainException("Coupon not found.");
            if(!coupon.IsRunning(nowUtc)) throw new DomainException("Coupon is not active.");
            var subtotal=checked(lines.Sum(x=>x.BaseLineIRR+x.WarrantyIRR*x.Item.Quantity));
            if(coupon.MinimumPurchaseIRR.HasValue&&subtotal<coupon.MinimumPurchaseIRR.Value) throw new DomainException("Minimum purchase for this coupon is not met.");
            if(coupon.NewCustomerOnly&&!await _pricing.IsNewCustomerAsync(customerId,ct)) throw new DomainException("Coupon is only for new customers.");
            if(coupon.MaxUses.HasValue&&await _pricing.GetCouponUsageCountAsync(coupon.Id,ct)>=coupon.MaxUses.Value) throw new DomainException("Coupon usage limit has been reached.");
            if(await _pricing.HasCustomerUsedCouponAsync(coupon.Id,customerId,ct)) throw new DomainException("Customer has already used this coupon.");
            var hasScope=await _pricing.CouponHasAnyScopeAsync(coupon.Id,ct);
            var eligible=0L;
            foreach(var l in lines)
            {
                var ok=!hasScope || await _pricing.CouponHasProductScopeAsync(coupon.Id,l.Data.Product.Id,ct) || await _pricing.CouponHasCategoryScopeAsync(coupon.Id,l.Data.Product.CategoryId,ct);
                if(ok) eligible=checked(eligible+l.BaseLineIRR);
            }
            if(eligible<=0) throw new DomainException("Coupon does not apply to cart items.");
            var discount=coupon.DiscountType==DiscountType.Percentage ? checked((long)Math.Floor(eligible*coupon.DiscountValue/100m)) : Math.Min(eligible,checked((long)coupon.DiscountValue));
            if(coupon.MaxDiscountAmountIRR.HasValue) discount=Math.Min(discount,coupon.MaxDiscountAmountIRR.Value);
            discount=Math.Min(discount,eligible);
            var remaining=discount;
            for(var i=0;i<lines.Count&&remaining>0;i++)
            {
                var l=lines[i];
                var ok=!hasScope || await _pricing.CouponHasProductScopeAsync(coupon.Id,l.Data.Product.Id,ct) || await _pricing.CouponHasCategoryScopeAsync(coupon.Id,l.Data.Product.CategoryId,ct);
                if(!ok) continue;
                var d=Math.Min(l.BaseLineIRR,remaining);
                var finalMerchandise=l.BaseLineIRR-d;
                lines[i]=l with { CouponDiscountIRR=d,FinalLineIRR=checked(finalMerchandise+l.WarrantyIRR*l.Item.Quantity),EffectiveUnitIRR=finalMerchandise/l.Item.Quantity };
                remaining-=d;
            }
            return new PricingResult(lines,subtotal,0,discount,checked(lines.Sum(x=>x.FinalLineIRR)),coupon);
        }
        return new PricingResult(lines,checked(lines.Sum(x=>x.BaseLineIRR+x.WarrantyIRR*x.Item.Quantity)),lines.Sum(x=>x.CampaignDiscountIRR),0,checked(lines.Sum(x=>x.FinalLineIRR)),null);
    }
}