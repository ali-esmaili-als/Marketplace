using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Pricing;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Pricing;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PricingServiceTests
{
    [Fact]
    public async Task Variant_price_overrides_product_price()
    {
        var product=Product.Create(1,1,1,"P","p",1000,true); product.Activate();
        var variant=ProductVariant.Create(2,1,"SKU","color:red",1500);
        var inventory=InventoryItem.Create(3,2,10);
        var input=(CartItem.Create(4,5,1,2,2),new CheckoutLineData(product,variant,inventory,null));
        var service=new PricingService(new FakePricingRepository());
        var result=await service.PriceAsync(9,1,1,new[]{input},null,DateTime.UtcNow);
        Assert.Equal(3000,result.SubtotalIRR);
        Assert.Equal(3000,result.TotalIRR);
    }

    [Fact]
    public async Task Percentage_coupon_respects_max_discount_cap()
    {
        var product=Product.Create(1,1,1,"P","p",1000000,false); product.Activate();
        var variant=ProductVariant.Create(2,1,"SKU","default",null);
        var inventory=InventoryItem.Create(3,2,10);
        var input=(CartItem.Create(4,5,1,2,2),new CheckoutLineData(product,variant,inventory,null));
        var repo=new FakePricingRepository{Coupon=Coupon.Create(10,1,1,"SAVE",DiscountType.Percentage,50,1000000)};
        var service=new PricingService(repo);
        var result=await service.PriceAsync(9,1,1,new[]{input},"SAVE",DateTime.UtcNow);
        Assert.Equal(1000000,result.CouponDiscountIRR);
        Assert.Equal(1000000,result.TotalIRR);
    }

    [Fact]
    public async Task Campaign_and_coupon_cannot_be_combined()
    {
        var product=Product.Create(1,1,1,"P","p",100000,false); product.Activate();
        var variant=ProductVariant.Create(2,1,"SKU","default",null);
        var inventory=InventoryItem.Create(3,2,10);
        var campaign=Campaign.Create(20,1,"Sale",DiscountType.Percentage,10,DateTime.UtcNow.AddHours(-1),DateTime.UtcNow.AddHours(1));
        var input=(CartItem.Create(4,5,1,2,1),new CheckoutLineData(product,variant,inventory,null));
        var service=new PricingService(new FakePricingRepository{Campaign=campaign,Coupon=Coupon.Create(10,1,1,"SAVE",DiscountType.Percentage,10)});
        await Assert.ThrowsAsync<Marketplace.Domain.Common.DomainException>(()=>service.PriceAsync(9,1,1,new[]{input},"SAVE",DateTime.UtcNow));
    }

    private sealed class FakePricingRepository : IPricingRepository
    {
        public Campaign? Campaign { get; init; }
        public Coupon? Coupon { get; init; }
        public Task<Campaign?> GetRunningCampaignAsync(long storeId,long productId,long? variantId,DateTime nowUtc,CancellationToken ct=default)=>Task.FromResult(Campaign);
        public Task<List<CampaignProduct>> GetCampaignTargetsAsync(long campaignId,CancellationToken ct=default)=>Task.FromResult(new List<CampaignProduct>());
        public Task<Coupon?> GetCouponAsync(long storeId,string code,CancellationToken ct=default)=>Task.FromResult(Coupon);
        public Task<int> GetCouponUsageCountAsync(long couponId,CancellationToken ct=default)=>Task.FromResult(0);
        public Task<bool> HasCustomerUsedCouponAsync(long couponId,long customerId,CancellationToken ct=default)=>Task.FromResult(false);
        public Task<bool> IsNewCustomerAsync(long customerId,CancellationToken ct=default)=>Task.FromResult(true);
        public Task<bool> CouponHasCategoryScopeAsync(long couponId,long categoryId,CancellationToken ct=default)=>Task.FromResult(false);
        public Task<bool> CouponHasProductScopeAsync(long couponId,long productId,CancellationToken ct=default)=>Task.FromResult(false);
        public Task<bool> CouponHasAnyScopeAsync(long couponId,CancellationToken ct=default)=>Task.FromResult(false);
        public Task<bool> ProductBelongsToStoreAsync(long productId,long storeId,CancellationToken ct=default)=>Task.FromResult(true);
        public Task<bool> VariantBelongsToProductAsync(long variantId,long productId,CancellationToken ct=default)=>Task.FromResult(true);
        public void AddCampaign(Campaign campaign){}
        public void AddCampaignTarget(CampaignProduct target){}
        public void AddCoupon(Coupon coupon){}
        public void AddCouponProduct(CouponProduct scope){}
        public void AddCouponCategory(CouponCategory scope){}
        public void AddCouponUsage(CouponUsage usage){}
        public Task<bool> HasOverlappingCampaignAsync(long storeId,DateTime startsAtUtc,DateTime endsAtUtc,long? exceptCampaignId=null,CancellationToken ct=default)=>Task.FromResult(false);
    }
}
