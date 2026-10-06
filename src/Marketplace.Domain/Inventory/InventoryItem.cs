using Marketplace.Domain.Common;

namespace Marketplace.Domain.Inventory;

public sealed class InventoryItem : AggregateRoot<long>
{
    private InventoryItem(){}
    public long ProductVariantId{get;private set;}
    public long StockQuantity{get;private set;}
    public long ReservedQuantity{get;private set;}
    public bool IsActive{get;private set;}
    public long AvailableQuantity=>Math.Max(0,StockQuantity-ReservedQuantity);

    public static InventoryItem Create(long id,long productVariantId,long initialStock=0)
    {
        if(id<=0||productVariantId<=0||initialStock<0)throw new DomainException("Invalid inventory item.");
        return new InventoryItem{Id=id,ProductVariantId=productVariantId,StockQuantity=initialStock,IsActive=true};
    }
    public void Increase(long quantity){Positive(quantity);StockQuantity=checked(StockQuantity+quantity);}
    public void Decrease(long quantity){Positive(quantity);if(AvailableQuantity<quantity)throw new DomainException("Insufficient available inventory.");StockQuantity-=quantity;}
    public void Reserve(long quantity){Positive(quantity);if(AvailableQuantity<quantity)throw new DomainException("Insufficient available inventory.");ReservedQuantity=checked(ReservedQuantity+quantity);}
    public void Release(long quantity){Positive(quantity);if(ReservedQuantity<quantity)throw new DomainException("Insufficient reserved inventory.");ReservedQuantity-=quantity;}
    public void ConsumeReservation(long quantity){Positive(quantity);if(ReservedQuantity<quantity||StockQuantity<quantity)throw new DomainException("Reservation cannot be consumed.");ReservedQuantity-=quantity;StockQuantity-=quantity;}
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
    private static void Positive(long q){if(q<=0)throw new DomainException("Quantity must be positive.");}
}