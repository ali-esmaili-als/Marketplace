using Marketplace.Domain.Carts;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Persistence;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserVerification> UserVerifications => Set<UserVerification>();
    public DbSet<UserLoginHistory> UserLoginHistories => Set<UserLoginHistory>();
    public DbSet<SellerPlan> SellerPlans => Set<SellerPlan>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<SellerBankAccount> SellerBankAccounts => Set<SellerBankAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<Marketplace.Domain.Catalog.Attribute> Attributes => Set<Marketplace.Domain.Catalog.Attribute>();
    public DbSet<AttributeValue> AttributeValues => Set<AttributeValue>();
    public DbSet<ProductTypeAttribute> ProductTypeAttributes => Set<ProductTypeAttribute>();
    public DbSet<ProductTypeAttributeValue> ProductTypeAttributeValues => Set<ProductTypeAttributeValue>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductVariantAttributeValue> ProductVariantAttributeValues => Set<ProductVariantAttributeValue>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductDiscount> ProductDiscounts => Set<ProductDiscount>();
    public DbSet<VariantDiscount> VariantDiscounts => Set<VariantDiscount>();
    public DbSet<WarrantyCategory> WarrantyCategories => Set<WarrantyCategory>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<ProductWarranty> ProductWarranties => Set<ProductWarranty>();
    public DbSet<ProductTypeWarrantyCategory> ProductTypeWarrantyCategories => Set<ProductTypeWarrantyCategory>();
    public DbSet<ShippingBenefit> ShippingBenefits => Set<ShippingBenefit>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignProduct> CampaignProducts => Set<CampaignProduct>();
    public DbSet<CampaignVariant> CampaignVariants => Set<CampaignVariant>();
    public DbSet<CampaignShippingBenefit> CampaignShippingBenefits => Set<CampaignShippingBenefit>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<DeliveryCode> DeliveryCodes => Set<DeliveryCode>();
    public DbSet<Commission> Commissions => Set<Commission>();
    public DbSet<BalanceTransaction> BalanceTransactions => Set<BalanceTransaction>();
    public DbSet<SellerBalance> SellerBalances => Set<SellerBalance>();
    public DbSet<SellerBalanceHold> SellerBalanceHolds => Set<SellerBalanceHold>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<CommissionReversal> CommissionReversals => Set<CommissionReversal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        MarketplaceModelConfiguration.Configure(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
