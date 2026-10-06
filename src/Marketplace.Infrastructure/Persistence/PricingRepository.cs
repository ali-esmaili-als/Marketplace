using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Pricing;

namespace Marketplace.Infrastructure.Persistence;

public sealed class PricingRepository(MarketplaceDbContext db) : IPricingRepository
{
    public async Task<Campaign?> GetRunningCampaignAsync(long storeId,long productId,long? variantId,DateTime nowUtc,CancellationToken ct=default)
    {
        var query=db.Campaigns.Where(c=>c.StoreId==storeId&&c.IsActive&&c.StartsAtUtc<=nowUtc&&c.EndsAtUtc>nowUtc)
            .Where(c=>db.CampaignProducts.Any(t=>t.CampaignId==c.Id&&t.ProductId==productId&&(t.ProductVariantId==null||t.ProductVariantId==variantId)));
        return await query.OrderBy(c=>c.Id).FirstOrDefaultAsync(ct);
    }
    public Task<List<CampaignProduct>> GetCampaignTargetsAsync(long campaignId,CancellationToken ct=default)=>db.CampaignProducts.Where(x=>x.CampaignId==campaignId).ToListAsync(ct);
    public Task<Coupon?> GetCouponAsync(long storeId,string code,CancellationToken ct=default)=>db.Coupons.SingleOrDefaultAsync(x=>x.StoreId==storeId&&x.Code==code.Trim().ToUpper(),ct);
    public Task<int> GetCouponUsageCountAsync(long couponId,CancellationToken ct=default)=>db.CouponUsages.CountAsync(x=>x.CouponId==couponId,ct);
    public Task<bool> HasCustomerUsedCouponAsync(long couponId,long customerId,CancellationToken ct=default)=>db.CouponUsages.AnyAsync(x=>x.CouponId==couponId&&x.CustomerId==customerId,ct);
    public Task<bool> IsNewCustomerAsync(long customerId,CancellationToken ct=default)=>db.Orders.AllAsync(x=>x.CustomerId!=customerId||x.Status==OrderStatus.PendingPayment||x.Status==OrderStatus.Cancelled,ct);
    public async Task<bool> CouponHasCategoryScopeAsync(long couponId,long categoryId,CancellationToken ct=default)
    {
        var path=await db.Categories.Where(x=>x.Id==categoryId).Select(x=>x.Path).SingleOrDefaultAsync(ct);
        if(path==null)return false;
        return await db.CouponCategories.Join(db.Categories,x=>x.CategoryId,c=>c.Id,(scope,category)=>new {CouponId=scope.CouponId,Path=category.Path})
            .AnyAsync(x=>x.CouponId==couponId&&(path==x.Path||path.StartsWith(x.Path+"-")),ct);
    }
    public Task<bool> CouponHasProductScopeAsync(long couponId,long productId,CancellationToken ct=default)=>db.CouponProducts.AnyAsync(x=>x.CouponId==couponId&&x.ProductId==productId,ct);
    public async Task<bool> CouponHasAnyScopeAsync(long couponId,CancellationToken ct=default)
    {
        var hasProduct=await db.CouponProducts.AnyAsync(x=>x.CouponId==couponId,ct);
        if(hasProduct)return true;
        return await db.CouponCategories.AnyAsync(x=>x.CouponId==couponId,ct);
    }
    public void AddCampaign(Campaign campaign)=>db.Campaigns.Add(campaign);
    public void AddCampaignTarget(CampaignProduct target)=>db.CampaignProducts.Add(target);
    public void AddCoupon(Coupon coupon)=>db.Coupons.Add(coupon);
    public void AddCouponProduct(CouponProduct scope)=>db.CouponProducts.Add(scope);
    public void AddCouponCategory(CouponCategory scope)=>db.CouponCategories.Add(scope);
    public void AddCouponUsage(CouponUsage usage)=>db.CouponUsages.Add(usage);
    public Task<bool> ProductBelongsToStoreAsync(long productId,long storeId,CancellationToken ct=default)=>db.Products.AnyAsync(x=>x.Id==productId&&x.StoreId==storeId,ct);
    public Task<bool> VariantBelongsToProductAsync(long variantId,long productId,CancellationToken ct=default)=>db.ProductVariants.AnyAsync(x=>x.Id==variantId&&x.ProductId==productId,ct);
    public Task<bool> HasOverlappingCampaignAsync(long storeId,DateTime startsAtUtc,DateTime endsAtUtc,long? exceptCampaignId=null,CancellationToken ct=default)=>db.Campaigns.AnyAsync(x=>x.StoreId==storeId&&x.IsActive&&(!exceptCampaignId.HasValue||x.Id!=exceptCampaignId.Value)&&startsAtUtc<x.EndsAtUtc&&endsAtUtc>x.StartsAtUtc,ct);
}