using Marketplace.Domain.Common;

namespace Marketplace.Domain.Inventory;

public sealed class InventoryItem : AggregateRoot<long>
{
    private InventoryItem() { }
    public long ProductId { get; private set; }
    public long ProductVariantId { get; private set; }
    public int StockQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static InventoryItem Create(long id,long productId,long productVariantId,int stockQuantity)
    {
        if(stockQuantity<0) throw new DomainException("Stock quantity cannot be negative.");
        return new InventoryItem { Id=id, ProductId=productId, ProductVariantId=productVariantId, StockQuantity=stockQuantity, UpdatedAtUtc=DateTime.UtcNow };
    }
    public int AvailableQuantity => StockQuantity-ReservedQuantity;
    public void IncreaseStock(int quantity){if(quantity<=0)throw new DomainException("Quantity must be positive.");StockQuantity=checked(StockQuantity+quantity);Touch();}
    public void DecreaseStock(int quantity){if(quantity<=0 || quantity>StockQuantity)throw new DomainException("Insufficient stock.");StockQuantity-=quantity;Touch();}
    public void Reserve(int quantity){if(quantity<=0 || AvailableQuantity<quantity)throw new DomainException("Insufficient available stock.");ReservedQuantity=checked(ReservedQuantity+quantity);Touch();}
    public void Release(int quantity){if(quantity<=0 || quantity>ReservedQuantity)throw new DomainException("Invalid reserved quantity.");ReservedQuantity-=quantity;Touch();}
    public void ConsumeReservation(int quantity){if(quantity<=0 || quantity>ReservedQuantity || quantity>StockQuantity)throw new DomainException("Invalid reservation consumption.");ReservedQuantity-=quantity;StockQuantity-=quantity;Touch();}
    private void Touch()=>UpdatedAtUtc=DateTime.UtcNow;
}
