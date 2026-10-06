using Marketplace.Domain.Common;

namespace Marketplace.Domain.Cart;

public sealed class CartItem : Entity<long>
{
    private CartItem() { }

    public long CartId { get; private set; }
    public long ProductId { get; private set; }
    public long ProductVariantId { get; private set; }
    public long? WarrantyId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static CartItem Create(long id, long cartId, long productId, long variantId, int quantity, long? warrantyId = null)
    {
        if (id <= 0 || cartId <= 0 || productId <= 0 || variantId <= 0 || quantity <= 0)
            throw new DomainException("Invalid cart item.");
        return new CartItem
        {
            Id=id, CartId=cartId, ProductId=productId, ProductVariantId=variantId,
            WarrantyId=warrantyId, Quantity=quantity, CreatedAtUtc=DateTime.UtcNow
        };
    }

    public void ChangeQuantity(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        Quantity = quantity;
    }

    public void ChangeWarranty(long? warrantyId) => WarrantyId = warrantyId;
}
