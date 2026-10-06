using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class VariantDiscount : Entity<long>
{
    private VariantDiscount() { }
    public long StoreId { get; private set; }
    public long ProductId { get; private set; }
    public long VariantId { get; private set; }
    public byte DiscountType { get; private set; }
    public long DiscountValue { get; private set; }
    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static VariantDiscount Create(long id,long storeId,long productId,long variantId,byte discountType,long discountValue,DateTime startAtUtc,DateTime endAtUtc)
    {
        if(discountType is < 1 or > 2) throw new DomainException("Invalid discount type.");
        if(discountValue<0) throw new DomainException("Discount value cannot be negative.");
        if(endAtUtc<=startAtUtc) throw new DomainException("Discount end must be after start.");
        return new VariantDiscount { Id=id, StoreId=storeId, ProductId=productId, VariantId=variantId, DiscountType=discountType, DiscountValue=discountValue, StartAtUtc=startAtUtc, EndAtUtc=endAtUtc, IsActive=true };
    }
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
