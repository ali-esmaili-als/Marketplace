using Marketplace.Domain.Common;

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
    public long UnitPriceIRR { get; private set; }
    public long WarrantyPriceIRR { get; private set; }
    public int Quantity { get; private set; }
    public long LineTotalIRR { get; private set; }

    public static OrderItem Create(long id,long orderId,long productId,long? variantId,string productName,string? variantSnapshot,
        long unitPriceIrr,int quantity,long warrantyId=0,string? warrantySnapshot=null,long warrantyPriceIrr=0)
    {
        if(id<=0||orderId<=0||productId<=0||string.IsNullOrWhiteSpace(productName)||unitPriceIrr<0||quantity<=0||warrantyId<0||warrantyPriceIrr<0)
            throw new DomainException("Invalid order item.");
        var line=checked((unitPriceIrr+warrantyPriceIrr)*quantity);
        return new OrderItem { Id=id,OrderId=orderId,ProductId=productId,VariantId=variantId,ProductNameSnapshot=productName.Trim(),
            VariantSnapshot=variantSnapshot?.Trim(),WarrantyId=warrantyId, WarrantySnapshot=warrantySnapshot?.Trim(),
            UnitPriceIRR=unitPriceIrr,WarrantyPriceIRR=warrantyPriceIrr,Quantity=quantity,LineTotalIRR=line };
    }
}