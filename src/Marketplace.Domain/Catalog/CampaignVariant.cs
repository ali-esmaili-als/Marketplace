using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class CampaignVariant : Entity<long>
{
    private CampaignVariant() { }
    public long CampaignId { get; private set; }
    public long StoreId { get; private set; }
    public long ProductId { get; private set; }
    public long VariantId { get; private set; }
    public byte DiscountType { get; private set; }
    public long DiscountValue { get; private set; }
    public static CampaignVariant Create(long id,long campaignId,long storeId,long productId,long variantId,byte discountType,long discountValue)
    {
        if(discountType is < 1 or > 2 || discountValue<0) throw new DomainException("Invalid campaign variant discount.");
        return new CampaignVariant { Id=id, CampaignId=campaignId, StoreId=storeId, ProductId=productId, VariantId=variantId, DiscountType=discountType, DiscountValue=discountValue };
    }
}
