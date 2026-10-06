using Marketplace.Domain.Common;
using Marketplace.Domain.Pricing;

namespace Marketplace.Domain.Orders;

public sealed class OrderItem : Entity<long>
{
    private OrderItem() { }
    public long OrderId { get; private set; }
    public long ProductId { get; private set; }
    public long? VariantId { get; private set; }
    public long WarrantyId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = null!;
    public string? VariantSnapshot { get; private set; }
    public string? WarrantySnapshot { get; private set; }
    public long BaseUnitPriceIRR { get; private set; }
    public long UnitPriceIRR { get; private set; }
    public long WarrantyPriceIRR { get; private set; }
    public long CampaignDiscountIRR { get; private set; }
    public long CouponDiscountIRR { get; private set; }
    public long? CampaignId { get; private set; }
    public string? CampaignNameSnapshot { get; private set; }
    public int Quantity { get; private set; }
    public long LineTotalIRR { get; private set; }

    public static OrderItem Create(long id,long orderId,long productId,long? variantId,string productName,string? variantSnapshot,long baseUnitPriceIrr,int quantity,long warrantyId=0,string? warrantySnapshot=null,long warrantyPriceIrr=0,long campaignDiscountIrr=0,long couponDiscountIrr=0,long? campaignId=null,string? campaignNameSnapshot=null)
    {
        if(id<=0||orderId<=0||productId<=0||string.IsNullOrWhiteSpace(productName)||baseUnitPriceIrr<0||quantity<=0||warrantyId<0||warrantyPriceIrr<0||campaignDiscountIrr<0||couponDiscountIrr<0)
            throw new DomainException("Invalid order item.");
        var merchandise=checked(baseUnitPriceIrr*quantity);
        if(campaignDiscountIrr+couponDiscountIrr>merchandise) throw new DomainException("Invalid item discounts.");
        var finalMerchandise=merchandise-campaignDiscountIrr-couponDiscountIrr;
        var unit=finalMerchandise/quantity;
        var line=checked(finalMerchandise+warrantyPriceIrr*quantity);
        return new OrderItem { Id=id,OrderId=orderId,ProductId=productId,VariantId=variantId,ProductNameSnapshot=productName.Trim(),VariantSnapshot=variantSnapshot?.Trim(),WarrantyId=warrantyId,WarrantySnapshot=warrantySnapshot?.Trim(),BaseUnitPriceIRR=baseUnitPriceIrr,UnitPriceIRR=unit,WarrantyPriceIRR=warrantyPriceIrr,CampaignDiscountIRR=campaignDiscountIrr,CouponDiscountIRR=couponDiscountIrr,CampaignId=campaignId,CampaignNameSnapshot=campaignNameSnapshot?.Trim(),Quantity=quantity,LineTotalIRR=line };
    }
}