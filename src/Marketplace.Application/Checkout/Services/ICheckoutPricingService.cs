using Marketplace.Application.Checkout.Ports;

namespace Marketplace.Application.Checkout.Services;

public sealed class CheckoutPricingService : ICheckoutPricingService
{
    public Task<CheckoutPricingResult> CalculateAsync(
        CheckoutCartSnapshot cart,
        long couponDiscountIRR = 0,
        CancellationToken cancellationToken = default)
    {
        if (cart.Items.Count == 0)
            throw new InvalidOperationException("Cart is empty.");

        long subtotal = 0, campaign = 0, direct = 0, warranty = 0, shippingGross = 0;
        var raw = new List<PricedCheckoutItem>(cart.Items.Count);

        foreach (var item in cart.Items)
        {
            if (item.Quantity <= 0 || item.UnitPriceIRR < 0)
                throw new InvalidOperationException("Invalid cart item.");

            var lineSubtotal = checked(item.UnitPriceIRR * item.Quantity);
            var campaignDiscount = CalculateDiscount(item.CampaignDiscount, lineSubtotal);
            var afterCampaign = Math.Max(0, lineSubtotal - campaignDiscount);
            var directRule = item.VariantDiscount ?? item.ProductDiscount;
            var directDiscount = CalculateDiscount(directRule, afterCampaign);
            var warrantyAmount = checked(item.WarrantyPriceIRR * item.Quantity);
            var shipping = Math.Max(0, item.AllocatedShippingIRR);

            subtotal = checked(subtotal + lineSubtotal);
            campaign = checked(campaign + campaignDiscount);
            direct = checked(direct + directDiscount);
            warranty = checked(warranty + warrantyAmount);
            shippingGross = checked(shippingGross + shipping);

            raw.Add(new PricedCheckoutItem(
                item.ProductId, item.ProductVariantId, item.Quantity, item.UnitPriceIRR,
                item.ProductNameSnapshot, item.VariantKeySnapshot, item.SkuSnapshot,
                item.WarrantyId, item.WarrantyNameSnapshot, warrantyAmount, lineSubtotal,
                campaignDiscount, directDiscount, 0, shipping, 0,
                item.CampaignDiscount?.CampaignId, item.CampaignDiscount?.CampaignName));
        }

        var eligible = Math.Max(0, subtotal - campaign - direct);
        var coupon = Math.Clamp(couponDiscountIRR, 0, eligible);
        var items = AllocateCoupon(raw, coupon, eligible);

        // Shipping rules are deliberately isolated behind AllocatedShippingIRR in the reader.
        // Campaign/coupon shipping-benefit orchestration will be added when the shipping policy port is introduced.
        const long shippingBenefit = 0;
        var shippingAmount = Math.Max(0, shippingGross - shippingBenefit);
        var total = checked(subtotal - campaign - direct - coupon + warranty + shippingAmount);

        items = items.Select(x => x with
        {
            FinalLineTotalIRR = checked(
                x.LineSubtotalIRR - x.CampaignDiscountIRR - x.DirectDiscountIRR
                - x.CouponDiscountIRR + x.WarrantyAmountIRR + x.AllocatedShippingIRR)
        }).ToList();

        return Task.FromResult(new CheckoutPricingResult(
            subtotal, campaign, direct, coupon, warranty,
            shippingGross, shippingBenefit, shippingAmount, total, items));
    }

    private static long CalculateDiscount(DiscountSnapshot? rule, long amount)
    {
        if (rule is null || amount <= 0) return 0;
        return rule.DiscountType switch
        {
            1 => Math.Min(amount, checked(amount * rule.DiscountValue / 100)),
            2 => Math.Min(amount, rule.DiscountValue),
            _ => throw new InvalidOperationException("Unknown discount type.")
        };
    }

    private static List<PricedCheckoutItem> AllocateCoupon(
        List<PricedCheckoutItem> items, long coupon, long eligible)
    {
        if (coupon == 0 || eligible == 0) return items;

        long allocated = 0;
        var result = new List<PricedCheckoutItem>(items.Count);
        var lastEligibleIndex = -1;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var eligibleLine = Math.Max(0, item.LineSubtotalIRR - item.CampaignDiscountIRR - item.DirectDiscountIRR);
            if (eligibleLine > 0) lastEligibleIndex = i;
            result.Add(item with { CouponDiscountIRR = 0 });
        }

        for (var i = 0; i < result.Count; i++)
        {
            var eligibleLine = Math.Max(0, result[i].LineSubtotalIRR - result[i].CampaignDiscountIRR - result[i].DirectDiscountIRR);
            if (eligibleLine == 0) continue;
            var share = Math.Min(eligibleLine, checked(coupon * eligibleLine / eligible));
            result[i] = result[i] with { CouponDiscountIRR = share };
            allocated += share;
        }

        if (lastEligibleIndex >= 0 && allocated < coupon)
        {
            var last = result[lastEligibleIndex];
            result[lastEligibleIndex] = last with
            {
                CouponDiscountIRR = checked(last.CouponDiscountIRR + coupon - allocated)
            };
        }

        return result;
    }
}


public interface ICheckoutPricingService
{
    Task<CheckoutPricingResult> CalculateAsync(
        CheckoutCartSnapshot cart,
        long couponDiscountIRR = 0,
        CancellationToken cancellationToken = default);
}

public sealed record CheckoutPricingResult(
    long SubTotalIRR,
    long CampaignDiscountIRR,
    long DirectDiscountIRR,
    long CouponDiscountIRR,
    long WarrantyAmountIRR,
    long ShippingGrossIRR,
    long ShippingBenefitIRR,
    long ShippingAmountIRR,
    long TotalAmountIRR,
    IReadOnlyList<PricedCheckoutItem> Items);

public sealed record PricedCheckoutItem(
    long ProductId,
    long ProductVariantId,
    int Quantity,
    long UnitPriceIRR,
    string ProductNameSnapshot,
    string? VariantKeySnapshot,
    string? SkuSnapshot,
    long? WarrantyId,
    string? WarrantyNameSnapshot,
    long WarrantyAmountIRR,
    long LineSubtotalIRR,
    long CampaignDiscountIRR,
    long DirectDiscountIRR,
    long CouponDiscountIRR,
    long AllocatedShippingIRR,
    long FinalLineTotalIRR,
    long? CampaignId,
    string? CampaignNameSnapshot);
