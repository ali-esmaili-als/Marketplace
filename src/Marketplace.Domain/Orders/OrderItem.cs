using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class OrderItem : Entity<long>
{
    private OrderItem() { }
    public long OrderId { get; private set; }
    public long ProductId { get; private set; }
    public long ProductVariantId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = null!;
    public string VariantKeySnapshot { get; private set; } = null!;
    public string? SkuSnapshot { get; private set; }
    public long UnitPriceIRR { get; private set; }
    public int Quantity { get; private set; }
    public long LineSubtotalIRR { get; private set; }
    public long CampaignDiscountIRR { get; private set; }
    public long DirectDiscountIRR { get; private set; }
    public long CouponDiscountIRR { get; private set; }
    public long? WarrantyId { get; private set; }
    public string? WarrantyNameSnapshot { get; private set; }
    public long WarrantyAmountIRR { get; private set; }
    public long AllocatedShippingIRR { get; private set; }
    public long FinalLineTotalIRR { get; private set; }
    public long? CampaignId { get; private set; }
    public string? CampaignNameSnapshot { get; private set; }

    public static OrderItem Create(long id,long orderId,long productId,long variantId,string productName,string variantKey,string? sku,long unitPrice,int quantity,long campaignDiscount,long directDiscount,long couponDiscount,long? warrantyId,string? warrantyName,long warrantyAmount,long allocatedShipping,long? campaignId,string? campaignName)
    {
        if(quantity<=0 || unitPrice<0 || campaignDiscount<0 || directDiscount<0 || couponDiscount<0 || warrantyAmount<0 || allocatedShipping<0) throw new DomainException("Invalid order item amount.");
        var subtotal=checked(unitPrice*quantity);
        var final=checked(subtotal-campaignDiscount-directDiscount-couponDiscount+warrantyAmount+allocatedShipping);
        if(final<0) throw new DomainException("Order item total cannot be negative.");
        return new OrderItem { Id=id, OrderId=orderId, ProductId=productId, ProductVariantId=variantId, ProductNameSnapshot=productName, VariantKeySnapshot=variantKey, SkuSnapshot=sku, UnitPriceIRR=unitPrice, Quantity=quantity, LineSubtotalIRR=subtotal, CampaignDiscountIRR=campaignDiscount, DirectDiscountIRR=directDiscount, CouponDiscountIRR=couponDiscount, WarrantyId=warrantyId, WarrantyNameSnapshot=warrantyName, WarrantyAmountIRR=warrantyAmount, AllocatedShippingIRR=allocatedShipping, FinalLineTotalIRR=final, CampaignId=campaignId, CampaignNameSnapshot=campaignName };
    }
}
