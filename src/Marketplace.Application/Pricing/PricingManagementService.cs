using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Pricing;
namespace Marketplace.Application.Pricing;
public sealed class PricingManagementService
{
    private readonly IPricingRepository _repo; private readonly IIdGenerator _ids; private readonly IUnitOfWork _uow;
    public PricingManagementService(IPricingRepository repo,IIdGenerator ids,IUnitOfWork uow){_repo=repo;_ids=ids;_uow=uow;}
    public async Task<long> CreateCampaignAsync(long storeId,string name,DiscountType type,decimal value,DateTime startsAtUtc,DateTime endsAtUtc,IReadOnlyCollection<(long ProductId,long? VariantId)> targets,CancellationToken ct=default)
    {
        if(await _repo.HasOverlappingCampaignAsync(storeId,startsAtUtc,endsAtUtc,null,ct)) throw new DomainException("Campaign dates overlap another campaign for this store.");
        var id=await _ids.NextAsync(ct); _repo.AddCampaign(Campaign.Create(id,storeId,name,type,value,startsAtUtc,endsAtUtc));
        foreach(var t in targets.Distinct()) _repo.AddCampaignTarget(CampaignProduct.Create(await _ids.NextAsync(ct),id,t.ProductId,t.VariantId));
        await _uow.SaveChangesAsync(ct); return id;
    }
    public async Task<long> CreateCouponAsync(long sellerId,long storeId,string code,DiscountType type,decimal value,long? maxDiscount,long? minimumPurchase,int? maxUses,bool newCustomerOnly,DateTime? starts,DateTime? ends,IReadOnlyCollection<long> productIds,IReadOnlyCollection<long> categoryIds,CancellationToken ct=default)
    {
        var coupon=Coupon.Create(await _ids.NextAsync(ct),sellerId,storeId,code,type,value,maxDiscount,minimumPurchase,maxUses,newCustomerOnly,starts,ends);
        _repo.AddCoupon(coupon);
        foreach(var id in productIds.Distinct()) _repo.AddCouponProduct(CouponProduct.Create(await _ids.NextAsync(ct),coupon.Id,id));
        foreach(var id in categoryIds.Distinct()) _repo.AddCouponCategory(CouponCategory.Create(await _ids.NextAsync(ct),coupon.Id,id));
        await _uow.SaveChangesAsync(ct); return coupon.Id;
    }
}