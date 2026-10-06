using Marketplace.Domain.Common;

namespace Marketplace.Domain.Carts;

public sealed class CartItem : Entity<long>
{
    private CartItem() { }
    public long CartId { get; private set; }
    public long ProductVariantId { get; private set; }
    public long ProductId { get; private set; }
    public long StoreId { get; private set; }
    public int Quantity { get; private set; }
    public long? CachedUnitPriceIRR { get; private set; }
    public long? WarrantyId { get; private set; }
    public long WarrantyPriceIRR { get; private set; }

    public static CartItem Create(long id,long cartId,long productVariantId,long productId,long storeId,int quantity,long? cachedUnitPriceIrr,long? warrantyId,long warrantyPriceIrr)
    {
        if(quantity<=0) throw new DomainException("Cart quantity must be positive.");
        if(cachedUnitPriceIrr is < 0 || warrantyPriceIrr<0) throw new DomainException("Cart prices cannot be negative.");
        return new CartItem { Id=id, CartId=cartId, ProductVariantId=productVariantId, ProductId=productId, StoreId=storeId, Quantity=quantity, CachedUnitPriceIRR=cachedUnitPriceIrr, WarrantyId=warrantyId, WarrantyPriceIRR=warrantyPriceIrr };
    }
    public void ChangeQuantity(int quantity){if(quantity<=0)throw new DomainException("Cart quantity must be positive.");Quantity=quantity;}
    public void SetWarranty(long? warrantyId,long warrantyPriceIrr){if(warrantyPriceIrr<0)throw new DomainException("Warranty price cannot be negative.");WarrantyId=warrantyId;WarrantyPriceIRR=warrantyPriceIrr;}
    public void SetCachedUnitPrice(long? price){if(price is < 0)throw new DomainException("Price cannot be negative.");CachedUnitPriceIRR=price;}
}
