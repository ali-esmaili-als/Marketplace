using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class CampaignProduct : Entity<long>
{
    private CampaignProduct() { }
    public long CampaignId { get; private set; }
    public long StoreId { get; private set; }
    public long ProductId { get; private set; }
    public byte DiscountType { get; private set; }
    public long DiscountValue { get; private set; }
    public static CampaignProduct Create(long id,long campaignId,long storeId,long productId,byte discountType,long discountValue)
    {
        if(discountType is < 1 or > 2 || discountValue<0) throw new DomainException("Invalid campaign product discount.");
        return new CampaignProduct { Id=id, CampaignId=campaignId, StoreId=storeId, ProductId=productId, DiscountType=discountType, DiscountValue=discountValue };
    }
}
