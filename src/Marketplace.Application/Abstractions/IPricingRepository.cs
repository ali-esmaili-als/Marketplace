using Marketplace.Domain.Pricing;
namespace Marketplace.Application.Abstractions;
public interface IPricingRepository
{
    Task<Campaign?> GetRunningCampaignAsync(long storeId,long productId,long? variantId,DateTime nowUtc,CancellationToken ct=default);
    Task<List<CampaignProduct>> GetCampaignTargetsAsync(long campaignId,CancellationToken ct=default);
    Task<Coupon?> GetCouponAsync(long storeId,string code,CancellationToken ct=default);
    Task<int> GetCouponUsageCountAsync(long couponId,CancellationToken ct=default);
    Task<bool> HasCustomerUsedCouponAsync(long couponId,long customerId,CancellationToken ct=default);
    Task<bool> IsNewCustomerAsync(long customerId,CancellationToken ct=default);
    Task<bool> CouponHasCategoryScopeAsync(long couponId,long categoryId,CancellationToken ct=default);
    Task<bool> CouponHasProductScopeAsync(long couponId,long productId,CancellationToken ct=default);
    Task<bool> CouponHasAnyScopeAsync(long couponId,CancellationToken ct=default);
    void AddCampaign(Campaign campaign);
    void AddCampaignTarget(CampaignProduct target);
    void AddCoupon(Coupon coupon);
    void AddCouponProduct(CouponProduct scope);
    void AddCouponCategory(CouponCategory scope);
    void AddCouponUsage(CouponUsage usage);
    Task<bool> HasOverlappingCampaignAsync(long storeId,DateTime startsAtUtc,DateTime endsAtUtc,long? exceptCampaignId=null,CancellationToken ct=default);
}