using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class CampaignShippingBenefit : Entity<long>
{
    private CampaignShippingBenefit() { }
    public long CampaignId { get; private set; }
    public long ShippingBenefitId { get; private set; }
    public static CampaignShippingBenefit Create(long id,long campaignId,long shippingBenefitId)
        => new() { Id=id, CampaignId=campaignId, ShippingBenefitId=shippingBenefitId };
}
