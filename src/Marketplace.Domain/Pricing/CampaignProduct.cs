using Marketplace.Domain.Common;

namespace Marketplace.Domain.Pricing;

public sealed class CampaignProduct : Entity<long>
{
    private CampaignProduct() { }
    public long CampaignId { get; private set; }
    public long ProductId { get; private set; }
    public long? ProductVariantId { get; private set; }

    public static CampaignProduct Create(long id,long campaignId,long productId,long? productVariantId=null)
    {
        if(id<=0||campaignId<=0||productId<=0||(productVariantId.HasValue&&productVariantId<=0))
            throw new DomainException("Invalid campaign target.");
        return new CampaignProduct { Id=id,CampaignId=campaignId,ProductId=productId,ProductVariantId=productVariantId };
    }
}