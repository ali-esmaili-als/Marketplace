using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Pricing;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Pricing;

public sealed class PricingManagementService
{
    private readonly IPricingRepository _repo; private readonly ISellerManagementRepository _sellers; private readonly IIdGenerator _ids; private readonly IUnitOfWork _uow;
    public PricingManagementService(IPricingRepository repo,ISellerManagementRepository sellers,IIdGenerator ids,IUnitOfWork uow){_repo=repo;_sellers=sellers;_ids=ids;_uow=uow;}

    public async Task<long> CreateCampaignAsync(long sellerId,long storeId,string name,DiscountType type,decimal value,DateTime startsAtUtc,DateTime endsAtUtc,IReadOnlyCollection<(long ProductId,long? VariantId)> targets,CancellationToken ct=default)
    {
        var seller=await _sellers.GetSellerAsync(sellerId,ct)??throw new DomainException("Seller not found.");
        if(seller.Status!=SellerStatus.Active) throw new DomainException("Seller is not active.");
        if(!await _sellers.StoreBelongsToSellerAsync(storeId,sellerId,ct)) throw new DomainException("Store does not belong to seller.");
        if(await _repo.HasOverlappingCampaignAsync(storeId,startsAtUtc,endsAtUtc,null,ct)) throw new DomainException("Campaign dates overlap another campaign for this store.");

        return await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            if(await _repo.HasOverlappingCampaignAsync(storeId,startsAtUtc,endsAtUtc,null,token)) throw new DomainException("Campaign dates overlap another campaign for this store.");
            foreach(var t in targets.Distinct())
        {
            if(!await _repo.ProductBelongsToStoreAsync(t.ProductId,storeId,token)) throw new DomainException("Campaign product does not belong to store.");
            if(t.VariantId.HasValue&&!await _repo.VariantBelongsToProductAsync(t.VariantId.Value,t.ProductId,token)) throw new DomainException("Campaign variant does not belong to product.");
        }
            var id=await _ids.NextAsync(token); _repo.AddCampaign(Campaign.Create(id,storeId,name,type,value,startsAtUtc,endsAtUtc));
            foreach(var t in targets.Distinct()) _repo.AddCampaignTarget(CampaignProduct.Create(await _ids.NextAsync(token),id,t.ProductId,t.VariantId));
            await _uow.SaveChangesAsync(token); return id;
        },ct);
    }

    public async Task<long> CreateCouponAsync(long sellerId,long storeId,string code,DiscountType type,decimal value,long? maxDiscount,long? minimumPurchase,int? maxUses,bool newCustomerOnly,DateTime? starts,DateTime? ends,IReadOnlyCollection<long> productIds,IReadOnlyCollection<long> categoryIds,CancellationToken ct=default)
    {
        var seller=await _sellers.GetSellerAsync(sellerId,ct)??throw new DomainException("Seller not found.");
        if(seller.Status!=SellerStatus.Active) throw new DomainException("Seller is not active.");
        if(!await _sellers.StoreBelongsToSellerAsync(storeId,sellerId,ct)) throw new DomainException("Store does not belong to seller.");

        foreach(var productId in productIds.Distinct())
            if(!await _repo.ProductBelongsToStoreAsync(productId,storeId,ct)) throw new DomainException("Coupon product does not belong to store.");

        var coupon=Coupon.Create(await _ids.NextAsync(ct),sellerId,storeId,code,type,value,maxDiscount,minimumPurchase,maxUses,newCustomerOnly,starts,ends);
        _repo.AddCoupon(coupon);
        foreach(var id in productIds.Distinct()) _repo.AddCouponProduct(CouponProduct.Create(await _ids.NextAsync(ct),coupon.Id,id));
        foreach(var id in categoryIds.Distinct()) _repo.AddCouponCategory(CouponCategory.Create(await _ids.NextAsync(ct),coupon.Id,id));
        await _uow.SaveChangesAsync(ct); return coupon.Id;
    }
}