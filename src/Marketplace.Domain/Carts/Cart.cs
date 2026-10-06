using Marketplace.Domain.Common;

namespace Marketplace.Domain.Carts;

public sealed class Cart : AggregateRoot<long>
{
    private readonly List<CartItem> _items=[];
    private Cart() { }
    public long CustomerId { get; private set; }
    public long StoreId { get; private set; }
    public CartStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<CartItem> Items=>_items.AsReadOnly();

    public static Cart Create(long id,long customerId,long storeId)
    {
        var now=DateTime.UtcNow;
        return new Cart { Id=id, CustomerId=customerId, StoreId=storeId, Status=CartStatus.Active, Version=1, CreatedAtUtc=now, UpdatedAtUtc=now };
    }
    public void AddItem(CartItem item)
    {
        if(item.StoreId!=StoreId) throw new DomainException("Cart item belongs to another store.");
        var existing=_items.FirstOrDefault(x=>x.ProductVariantId==item.ProductVariantId);
        if(existing is null) _items.Add(item); else existing.ChangeQuantity(existing.Quantity+item.Quantity);
        Touch();
    }
    public void RemoveItem(long productVariantId){_items.RemoveAll(x=>x.ProductVariantId==productVariantId);Touch();}
    public void MarkConverted(){Status=CartStatus.Converted;Touch();}
    public void Abandon(){Status=CartStatus.Abandoned;Touch();}
    public void Reactivate(){Status=CartStatus.Active;Touch();}
    private void Touch(){Version++;UpdatedAtUtc=DateTime.UtcNow;}
}
