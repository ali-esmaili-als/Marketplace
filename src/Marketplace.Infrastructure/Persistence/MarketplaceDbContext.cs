using Microsoft.EntityFrameworkCore;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Sellers;
using Marketplace.Domain.Shipping;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Pricing;
using Marketplace.Domain.Notifications;

namespace Marketplace.Infrastructure.Persistence;

public sealed class MarketplaceDbContext : DbContext
{
    public MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<SellerBankAccount> SellerBankAccounts => Set<SellerBankAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductAttributeAssignment> ProductAttributeAssignments => Set<ProductAttributeAssignment>();
    public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<ProductWarranty> ProductWarranties => Set<ProductWarranty>();

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentReconciliationAudit> PaymentReconciliationAudits => Set<PaymentReconciliationAudit>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryCode> DeliveryCodes => Set<DeliveryCode>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<SellerBalance> SellerBalances => Set<SellerBalance>();
    public DbSet<SellerBalanceHold> SellerBalanceHolds => Set<SellerBalanceHold>();
    public DbSet<BalanceTransaction> BalanceTransactions => Set<BalanceTransaction>();
    public DbSet<Commission> Commissions => Set<Commission>();
    public DbSet<CommissionReversal> CommissionReversals => Set<CommissionReversal>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<PaymentProviderSetting> PaymentProviderSettings => Set<PaymentProviderSetting>();
    public DbSet<DeliveryCity> DeliveryCities => Set<DeliveryCity>();
    public DbSet<StoreShippingCity> StoreShippingCities => Set<StoreShippingCity>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<UserRule> UserRules => Set<UserRule>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignProduct> CampaignProducts => Set<CampaignProduct>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();
    public DbSet<CouponCategory> CouponCategories => Set<CouponCategory>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SmsProviderSetting> SmsProviderSettings => Set<SmsProviderSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users"); e.HasKey(x => x.Id);
            e.Property(x => x.Mobile).HasMaxLength(30).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Mobile).IsUnique();
            e.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("Roles"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<Rule>(e =>
        {
            e.ToTable("Rules"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(150).IsRequired();
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.ActionType).HasConversion<byte>();
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<UserRule>(e =>
        {
            e.ToTable("UserRules"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.RuleId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Rule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("UserRoleAssignments"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Seller>(e =>
        {
            e.ToTable("Sellers"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.MaxStoreCount).IsRequired();
        });
        b.Entity<Store>(e =>
        {
            e.ToTable("Stores"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(250).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasIndex(x => new { x.SellerId, x.Slug }).IsUnique();
            e.HasIndex(x => x.SellerId);
        });
        b.Entity<SellerBankAccount>(e =>
        {
            e.ToTable("SellerBankAccounts"); e.HasKey(x => x.Id);
            e.Property(x => x.BankName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Iban).HasMaxLength(34).IsRequired();
            e.Property(x => x.AccountHolderName).HasMaxLength(250).IsRequired();
            e.HasIndex(x => new { x.SellerId, x.Iban }).IsUnique();
            e.HasIndex(x => new { x.SellerId, x.IsDefault });
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Categories"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(250).IsRequired();
            e.Property(x => x.Path).HasColumnType("varchar(850)").IsRequired();
            e.HasIndex(x => new { x.ParentCategoryId, x.Slug }).IsUnique();
        });
        b.Entity<Product>(e =>
        {
            e.ToTable("Products"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(350).IsRequired();
            e.Property(x => x.Description).HasMaxLength(5000);
            e.HasIndex(x => new { x.StoreId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Status });
        });
        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("ProductVariants"); e.HasKey(x => x.Id);
            e.Property(x => x.SKU).HasMaxLength(150).IsRequired();
            e.Property(x => x.VariantKey).HasMaxLength(1000).IsRequired();
            e.Property(x => x.VariantKeyHash).HasColumnType("binary(32)").ValueGeneratedOnAddOrUpdate();
            e.HasIndex(x => new { x.ProductId, x.SKU }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.VariantKeyHash }).IsUnique();
        });
        b.Entity<ProductAttribute>(e =>
        {
            e.ToTable("ProductAttributes"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Slug }).IsUnique();
        });
        b.Entity<ProductAttributeValue>(e =>
        {
            e.ToTable("ProductAttributeValues"); e.HasKey(x => x.Id);
            e.Property(x => x.Value).HasMaxLength(150).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.ProductAttributeId, x.Slug }).IsUnique();
        });
        b.Entity<ProductAttributeAssignment>(e =>
        {
            e.ToTable("ProductAttributeAssignments"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductId, x.ProductAttributeId }).IsUnique();
        });
        b.Entity<VariantAttributeValue>(e =>
        {
            e.ToTable("VariantAttributeValues"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductVariantId, x.ProductAttributeValueId }).IsUnique();
        });
        b.Entity<Warranty>(e =>
        {
            e.ToTable("Warranties"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();
        });
        b.Entity<ProductWarranty>(e =>
        {
            e.ToTable("ProductWarranties"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductId, x.WarrantyId }).IsUnique();
        });

        b.Entity<Cart>(e =>
        {
            e.ToTable("Carts"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.CustomerId).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.CustomerId });
        });
        b.Entity<CartItem>(e =>
        {
            e.ToTable("CartItems"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CartId, x.ProductVariantId, x.WarrantyId }).IsUnique();
        });

        b.Entity<Order>(e => { e.ToTable("Orders"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.SubtotalAmountIRR).IsRequired(); e.Property(x => x.CouponCodeSnapshot).HasMaxLength(100); e.Property(x => x.DestinationCityNameSnapshot).HasMaxLength(200);
            e.Property(x => x.DestinationProvinceNameSnapshot).HasMaxLength(200); e.HasIndex(x => new { x.SellerId, x.Status }); e.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc }); e.HasIndex(x => x.DestinationCityId); });
        b.Entity<OrderItem>(e => { e.ToTable("OrderItems"); e.HasKey(x => x.Id); e.Property(x => x.ProductNameSnapshot).HasMaxLength(300).IsRequired(); e.Property(x => x.VariantSnapshot).HasMaxLength(1000); e.Property(x => x.WarrantySnapshot).HasMaxLength(500); e.Property(x => x.CampaignNameSnapshot).HasMaxLength(250); e.HasIndex(x => x.OrderId); });
        b.Entity<Payment>(e => { e.ToTable("Payments"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Provider).HasMaxLength(100); e.Property(x => x.Authority).HasMaxLength(200); e.Property(x => x.ReferenceNumber).HasMaxLength(200); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => x.Authority); });
        b.Entity<PaymentReconciliationAudit>(e =>
        {
            e.ToTable("PaymentReconciliationAudits"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.Note).HasMaxLength(2000).IsRequired();
            e.Property(x => x.BankReference).HasMaxLength(200);
            e.HasIndex(x => new { x.PaymentId, x.CreatedAtUtc });
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PaymentTransaction>(e => { e.ToTable("PaymentTransactions"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Provider).HasMaxLength(100).IsRequired(); e.Property(x => x.Authority).HasMaxLength(200); e.Property(x => x.Reference).HasMaxLength(200); e.HasIndex(x => new { x.PaymentId, x.Status }); });
        b.Entity<Delivery>(e => { e.ToTable("Deliveries"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.ConfirmationReference).HasMaxLength(200); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); });
        b.Entity<DeliveryCode>(e=>{e.ToTable("DeliveryCodes");e.HasKey(x=>x.Id);e.Property(x=>x.CodeHash).HasColumnType("binary(32)").IsRequired();e.HasIndex(x=>x.OrderId).IsUnique();e.HasIndex(x=>new{x.ExpiresAtUtc,x.UsedAtUtc});});
        b.Entity<Refund>(e => { e.ToTable("Refunds"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasConversion<byte>(); e.Property(x => x.ProviderReference).HasMaxLength(200); e.Property(x => x.FailureReason).HasMaxLength(1000); e.HasIndex(x => new { x.OrderId, x.Status }); });
        b.Entity<Complaint>(e => { e.ToTable("Complaints"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); e.Property(x => x.ResolutionNote).HasMaxLength(4000); e.HasIndex(x => new { x.OrderId, x.Status }); });
        b.Entity<InventoryItem>(e => { e.ToTable("InventoryItems"); e.HasKey(x => x.Id); e.HasIndex(x => x.ProductVariantId).IsUnique(); });
        b.Entity<InventoryReservation>(e => { e.ToTable("InventoryReservations"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.HasIndex(x => new { x.OrderId, x.Status }); e.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); });
        b.Entity<SellerBalance>(e => { e.ToTable("SellerBalances"); e.HasKey(x => x.Id); e.HasIndex(x => x.SellerId).IsUnique(); });
        b.Entity<SellerBalanceHold>(e => { e.ToTable("SellerBalanceHolds"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasMaxLength(500).IsRequired(); e.HasIndex(x => new { x.OrderId, x.Status }); });
        b.Entity<BalanceTransaction>(e => { e.ToTable("BalanceTransactions"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<byte>(); e.Property(x=>x.Bucket).HasConversion<byte>(); e.Property(x => x.Reference).HasMaxLength(200); e.HasIndex(x => new { x.SellerId, x.CreatedAtUtc }); e.HasIndex(x => new { x.OrderId, x.Type }); });
        b.Entity<Commission>(e => { e.ToTable("Commissions"); e.HasKey(x => x.Id); e.Property(x => x.CommissionRate).HasPrecision(9,4); e.HasIndex(x => x.OrderId).IsUnique(); });
        b.Entity<CommissionReversal>(e => { e.ToTable("CommissionReversals"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.CommissionId, x.RefundId }).IsUnique(); });
        b.Entity<Settlement>(e =>
        {
            e.ToTable("Settlements"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.BankNameSnapshot).HasMaxLength(150).IsRequired();
            e.Property(x => x.IbanSnapshot).HasMaxLength(34).IsRequired();
            e.Property(x => x.AccountHolderNameSnapshot).HasMaxLength(250).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.FailureReason).HasMaxLength(1000);
            e.HasIndex(x => new { x.SellerId, x.Status });
            e.HasIndex(x => new { x.Status, x.RequestedAtUtc });
        });
        b.Entity<PaymentProviderSetting>(e =>
        {
            e.ToTable("PaymentProviderSettings"); e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasConversion<byte>();
            e.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ConfigurationJson).HasColumnType("nvarchar(max)").IsRequired();
            e.HasIndex(x => x.Provider).IsUnique();
            e.HasIndex(x => new { x.IsEnabled, x.IsVisible, x.SortOrder });
        });
        b.Entity<Campaign>(e =>
        {
            e.ToTable("Campaigns"); e.HasKey(x=>x.Id);
            e.Property(x=>x.Name).HasMaxLength(250).IsRequired();
            e.Property(x=>x.DiscountType).HasConversion<byte>();
            e.Property(x=>x.DiscountValue).HasPrecision(18,4);
            e.HasIndex(x=>new { x.StoreId,x.StartsAtUtc,x.EndsAtUtc });
            e.HasIndex(x=>new { x.StoreId,x.IsActive });
        });
        b.Entity<CampaignProduct>(e =>
        {
            e.ToTable("CampaignProducts"); e.HasKey(x=>x.Id);
            e.HasIndex(x=>new { x.CampaignId,x.ProductId,x.ProductVariantId }).IsUnique();
            e.HasIndex(x=>new { x.ProductId,x.ProductVariantId });
        });
        b.Entity<Coupon>(e =>
        {
            e.ToTable("Coupons"); e.HasKey(x=>x.Id);
            e.Property(x=>x.Code).HasMaxLength(100).IsRequired();
            e.Property(x=>x.DiscountType).HasConversion<byte>();
            e.Property(x=>x.DiscountValue).HasPrecision(18,4);
            e.HasIndex(x=>new { x.StoreId,x.Code }).IsUnique();
            e.HasIndex(x=>new { x.StoreId,x.IsActive });
        });
        b.Entity<CouponProduct>(e =>
        {
            e.ToTable("CouponProducts"); e.HasKey(x=>x.Id);
            e.HasIndex(x=>new { x.CouponId,x.ProductId }).IsUnique();
        });
        b.Entity<CouponCategory>(e =>
        {
            e.ToTable("CouponCategories"); e.HasKey(x=>x.Id);
            e.HasIndex(x=>new { x.CouponId,x.CategoryId }).IsUnique();
        });
        b.Entity<CouponUsage>(e =>
        {
            e.ToTable("CouponUsages"); e.HasKey(x=>x.Id);
            e.HasIndex(x=>new { x.CouponId,x.CustomerId }).IsUnique();
            e.HasIndex(x=>x.OrderId).IsUnique();
        });

        b.Entity<Notification>(e => { e.ToTable("Notifications"); e.HasKey(x=>x.Id); e.Property(x=>x.Channel).HasConversion<byte>(); e.Property(x=>x.Status).HasConversion<byte>(); e.Property(x=>x.Title).HasMaxLength(250).IsRequired(); e.Property(x=>x.Body).HasMaxLength(4000).IsRequired(); e.Property(x=>x.ReferenceType).HasMaxLength(100); e.HasIndex(x=>new{x.UserId,x.Status,x.CreatedAtUtc}); });
        b.Entity<SmsProviderSetting>(e => { e.ToTable("SmsProviderSettings"); e.HasKey(x=>x.Id); e.Property(x=>x.Provider).HasMaxLength(50).IsRequired(); e.Property(x=>x.DisplayName).HasMaxLength(150).IsRequired(); e.HasIndex(x=>x.Provider).IsUnique(); e.HasIndex(x=>new{x.IsEnabled,x.IsVisible,x.SortOrder}); });

        b.Entity<DeliveryCity>(e =>
        {
            e.ToTable("DeliveryCities"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ProvinceName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.IsActive, x.ProvinceName, x.Name });
        });
        b.Entity<StoreShippingCity>(e =>
        {
            e.ToTable("StoreShippingCities"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StoreId, x.CityId }).IsUnique();
            e.HasIndex(x => x.StoreId);
            e.HasIndex(x => x.CityId);
            e.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<DeliveryCity>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
