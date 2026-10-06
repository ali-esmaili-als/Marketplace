using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductDiscount : Entity<long>
{
    private ProductDiscount() { }
    public long StoreId { get; private set; }
    public long ProductId { get; private set; }
    public byte DiscountType { get; private set; }
    public long DiscountValue { get; private set; }
    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static ProductDiscount Create(long id, long storeId, long productId, byte discountType, long discountValue, DateTime startAtUtc, DateTime endAtUtc)
    {
        Validate(discountType, discountValue, startAtUtc, endAtUtc);
        return new ProductDiscount { Id=id, StoreId=storeId, ProductId=productId, DiscountType=discountType, DiscountValue=discountValue, StartAtUtc=startAtUtc, EndAtUtc=endAtUtc, IsActive=true };
    }
    public void Activate() => IsActive=true;
    public void Deactivate() => IsActive=false;
    private static void Validate(byte type,long value,DateTime start,DateTime end)
    { if(type is < 1 or > 2) throw new DomainException("Invalid discount type."); if(value<0) throw new DomainException("Discount value cannot be negative."); if(end<=start) throw new DomainException("Discount end must be after start."); }
}
