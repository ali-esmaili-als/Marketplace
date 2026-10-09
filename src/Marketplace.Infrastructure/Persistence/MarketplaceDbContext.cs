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
    public DbSet<Marketplace.Domain.Shipping.Shipment> Shipments => Set<Marketplace.Domain.Shipping.Shipment>();
    public DbSet<Marketplace.Domain.Shipping.ShipmentTrackingEvent> ShipmentTrackingEvents => Set<Marketplace.Domain.Shipping.ShipmentTrackingEvent>();
    public DbSet<DeliveryCode> DeliveryCodes => Set<DeliveryCode>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundReconciliationAudit> RefundReconciliationAudits => Set<RefundReconciliationAudit>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<SellerBalance> SellerBalances => Set<SellerBalance>();
    public DbSet<SellerBalanceHold> SellerBalanceHolds => Set<SellerBalanceHold>();
    public DbSet<BalanceTransaction> BalanceTransactions => Set<BalanceTransaction>();
    public DbSet<Commission> Commissions => Set<Commission>();
    public DbSet<CommissionReversal> CommissionReversals => Set<CommissionReversal>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementReconciliationAudit> SettlementReconciliationAudits => Set<SettlementReconciliationAudit>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PaymentProviderSetting> PaymentProviderSettings => Set<PaymentProviderSetting>();
    public DbSet<Marketplace.Domain.Auditing.AdminAuditEvent> AdminAuditEvents => Set<Marketplace.Domain.Auditing.AdminAuditEvent>();
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
            e.ToTable("Sellers", t => { t.HasCheckConstraint("CK_Sellers_Commission", "CommissionRateBasisPoints BETWEEN 0 AND 10000 AND MinimumCommissionIRR >= 0"); t.HasCheckConstraint("CK_Sellers_MaxStore", "MaxStoreCount > 0"); }); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.MaxStoreCount).IsRequired();
        });
        b.Entity<Store>(e =>
        {
            e.ToTable("Stores", t => t.HasCheckConstraint("CK_Stores_Commission", "CommissionRateBasisPoints BETWEEN 0 AND 10000 AND MinimumCommissionIRR >= 0")); e.HasKey(x => x.Id);
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
            e.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Price", "BasePriceIRR >= 0")); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(350).IsRequired();
            e.Property(x => x.Description).HasMaxLength(5000);
            e.HasIndex(x => new { x.StoreId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Status });
        });
        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("ProductVariants", t => t.HasCheckConstraint("CK_ProductVariants_Price", "PriceIRR IS NULL OR PriceIRR >= 0")); e.HasKey(x => x.Id);
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
            e.ToTable("Warranties", t => t.HasCheckConstraint("CK_Warranties_Price", "PriceIRR >= 0")); e.HasKey(x => x.Id);
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
            e.ToTable("CartItems", t => t.HasCheckConstraint("CK_CartItems_Quantity", "Quantity > 0")); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CartId, x.ProductVariantId, x.WarrantyId }).IsUnique();
        });

        b.Entity<Order>(e => { e.ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Amounts", "SubtotalAmountIRR > 0 AND TotalAmountIRR > 0 AND TotalAmountIRR <= SubtotalAmountIRR AND SellerAmountIRR >= 0 AND SellerAmountIRR <= TotalAmountIRR")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.SubtotalAmountIRR).IsRequired(); e.Property(x => x.RequestKey).HasMaxLength(64); e.HasIndex(x => new { x.CustomerId, x.RequestKey }).IsUnique().HasFilter("[RequestKey] IS NOT NULL"); e.Property(x => x.CouponCodeSnapshot).HasMaxLength(100); e.Property(x => x.DestinationCityNameSnapshot).HasMaxLength(200);
            e.Property(x => x.DestinationProvinceNameSnapshot).HasMaxLength(200); e.HasIndex(x => new { x.SellerId, x.Status }); e.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc }); e.HasIndex(x => x.DestinationCityId); });
        b.Entity<OrderItem>(e => { e.ToTable("OrderItems", t => t.HasCheckConstraint("CK_OrderItems_Amounts", "BaseUnitPriceIRR >= 0 AND UnitPriceIRR >= 0 AND WarrantyPriceIRR >= 0 AND CampaignDiscountIRR >= 0 AND CouponDiscountIRR >= 0 AND Quantity > 0 AND LineTotalIRR >= 0")); e.HasKey(x => x.Id); e.Property(x => x.ProductNameSnapshot).HasMaxLength(300).IsRequired(); e.Property(x => x.VariantSnapshot).HasMaxLength(1000); e.Property(x => x.WarrantySnapshot).HasMaxLength(500); e.Property(x => x.CampaignNameSnapshot).HasMaxLength(250); e.HasIndex(x => x.OrderId); });
        b.Entity<Payment>(e => { e.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount", "AmountIRR > 0")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Provider).HasMaxLength(100); e.Property(x => x.Authority).HasMaxLength(200); e.Property(x => x.RedirectUrl).HasMaxLength(2048); e.Property(x => x.ReferenceNumber).HasMaxLength(200); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => x.Authority); });
        b.Entity<PaymentReconciliationAudit>(e =>
        {
            e.ToTable("PaymentReconciliationAudits"); e.HasKey(x => x.Id); e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.Note).HasMaxLength(2000).IsRequired();
            e.Property(x => x.BankReference).HasMaxLength(200);
            e.HasIndex(x => new { x.PaymentId, x.CreatedAtUtc });
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PaymentTransaction>(e => { e.ToTable("PaymentTransactions", t => t.HasCheckConstraint("CK_PaymentTransactions_Amount", "AmountIRR > 0")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Provider).HasMaxLength(100).IsRequired(); e.Property(x => x.Authority).HasMaxLength(200); e.Property(x => x.Reference).HasMaxLength(200); e.HasIndex(x => new { x.PaymentId, x.Status }); e.HasIndex(x => new { x.Provider, x.Authority }).IsUnique().HasFilter("[Authority] IS NOT NULL"); });
        b.Entity<Delivery>(e => { e.ToTable("Deliveries"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.ConfirmationReference).HasMaxLength(200); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); });
        b.Entity<DeliveryCode>(e=>{e.ToTable("DeliveryCodes");e.HasKey(x=>x.Id);e.Property(x=>x.CodeHash).HasColumnType("binary(32)").IsRequired();e.HasIndex(x=>x.OrderId).IsUnique();e.HasIndex(x=>new{x.ExpiresAtUtc,x.UsedAtUtc});});
        b.Entity<Marketplace.Domain.Shipping.Shipment>(e =>
        {
            e.ToTable("Shipments", t => t.HasCheckConstraint("CK_Shipments_Tracking", "LEN(CarrierName) > 0 AND LEN(TrackingNumber) > 0"));
            e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.CarrierName).HasMaxLength(150).IsRequired();
            e.Property(x => x.TrackingNumber).HasMaxLength(150).IsRequired();
            e.Property(x => x.TrackingUrl).HasMaxLength(1000);
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.SellerId, x.Status, x.UpdatedAtUtc });
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Marketplace.Domain.Shipping.ShipmentTrackingEvent>(e =>
        {
            e.ToTable("ShipmentTrackingEvents", t => t.HasCheckConstraint("CK_ShipmentTrackingEvents_Description", "LEN(Description) > 0"));
            e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Location).HasMaxLength(200);
            e.HasIndex(x => new { x.ShipmentId, x.OccurredAtUtc, x.Id });
            e.HasOne<Marketplace.Domain.Shipping.Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Refund>(e => { e.ToTable("Refunds", t => t.HasCheckConstraint("CK_Refunds_Amount", "AmountIRR > 0")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasConversion<byte>(); e.Property(x => x.ProviderReference).HasMaxLength(200); e.Property(x => x.FailureReason).HasMaxLength(1000); e.HasIndex(x => new { x.OrderId, x.Status }); e.HasIndex(x => x.OrderId).IsUnique().HasFilter("[Status] < 4"); });
        b.Entity<RefundReconciliationAudit>(e => { e.ToTable("RefundReconciliationAudits"); e.HasKey(x => x.Id); e.Property(x => x.Id).UseIdentityColumn(); e.Property(x => x.Note).HasMaxLength(2000).IsRequired(); e.Property(x => x.BankReference).HasMaxLength(200); e.HasIndex(x => new { x.RefundId, x.CreatedAtUtc }); e.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Complaint>(e => { e.ToTable("Complaints"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); e.Property(x => x.ResolutionNote).HasMaxLength(4000); e.HasIndex(x => new { x.OrderId, x.Status }); });
        b.Entity<InventoryItem>(e => { e.ToTable("InventoryItems", t => t.HasCheckConstraint("CK_InventoryItems_Qty", "StockQuantity >= 0 AND ReservedQuantity >= 0 AND ReservedQuantity <= StockQuantity")); e.HasKey(x => x.Id); e.HasIndex(x => x.ProductVariantId).IsUnique(); });
        b.Entity<InventoryReservation>(e => { e.ToTable("InventoryReservations", t => t.HasCheckConstraint("CK_InventoryReservations_Quantity", "Quantity > 0")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.HasIndex(x => new { x.OrderId, x.Status }); e.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); });
        b.Entity<SellerBalance>(e => { e.ToTable("SellerBalances", t => t.HasCheckConstraint("CK_SellerBalances_NonNegative", "AvailableIRR >= 0 AND PendingIRR >= 0 AND BlockedIRR >= 0 AND ReservedForSettlementIRR >= 0 AND LiabilityIRR >= 0")); e.HasKey(x => x.Id); e.HasIndex(x => x.SellerId).IsUnique(); });
        b.Entity<SellerBalanceHold>(e => { e.ToTable("SellerBalanceHolds", t => t.HasCheckConstraint("CK_SellerBalanceHolds_Amount", "AmountIRR > 0")); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<byte>(); e.Property(x => x.Reason).HasMaxLength(500).IsRequired(); e.HasIndex(x => new { x.OrderId, x.Status }); e.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL"); });
        b.Entity<BalanceTransaction>(e => { e.ToTable("BalanceTransactions", t => t.HasCheckConstraint("CK_BalanceTransactions_Amounts", "AmountIRR >= 0 AND BalanceBeforeIRR >= 0 AND BalanceAfterIRR >= 0")); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<byte>(); e.Property(x=>x.Bucket).HasConversion<byte>(); e.Property(x => x.Reference).HasMaxLength(200); e.HasIndex(x => new { x.SellerId, x.CreatedAtUtc }); e.HasIndex(x => new { x.OrderId, x.Type }); e.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL AND [Type] = 1"); e.HasIndex(x => x.RefundId).IsUnique().HasFilter("[RefundId] IS NOT NULL"); e.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Settlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Commission>(e => { e.ToTable("Commissions", t => t.HasCheckConstraint("CK_Commissions_Amounts", "OrderAmountIRR >= 0 AND CommissionRate >= 0 AND CommissionRate <= 100 AND MinimumCommissionIRR >= 0 AND CalculatedCommissionIRR >= 0 AND CommissionAmountIRR >= 0 AND SellerAmountIRR >= 0")); e.HasKey(x => x.Id); e.Property(x => x.CommissionRate).HasPrecision(9,4); e.HasIndex(x => x.OrderId).IsUnique(); });
        b.Entity<CommissionReversal>(e => { e.ToTable("CommissionReversals", t => t.HasCheckConstraint("CK_CommissionReversals_Amounts", "RefundAmountIRR > 0 AND ReversedCommissionIRR >= 0 AND ReversedCommissionIRR <= RefundAmountIRR")); e.HasKey(x => x.Id); e.HasIndex(x => new { x.CommissionId, x.RefundId }).IsUnique(); });
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages", t =>
            {
                t.HasCheckConstraint("CK_OutboxMessages_Status", "Status IN ('Pending','Processing','Processed','DeadLetter')");
                t.HasCheckConstraint("CK_OutboxMessages_Attempts", "Attempts >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).IsConcurrencyToken();
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.LockToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.Id });
            e.HasIndex(x => x.MessageId).IsUnique();
        });
        b.Entity<SettlementReconciliationAudit>(e => { e.ToTable("SettlementReconciliationAudits"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedOnAdd(); e.Property(x => x.Note).HasMaxLength(2000).IsRequired(); e.Property(x => x.BankReference).HasMaxLength(200); e.HasIndex(x => new { x.SettlementId, x.CreatedAtUtc }); e.HasOne<Settlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Settlement>(e =>
        {
            e.ToTable("Settlements", t => t.HasCheckConstraint("CK_Settlements_Amount", "AmountIRR > 0")); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<byte>();
            e.Property(x => x.RequestKey).HasMaxLength(64);
            e.HasIndex(x => new { x.SellerId, x.RequestKey }).IsUnique().HasFilter("[RequestKey] IS NOT NULL");
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
        b.Entity<Marketplace.Domain.Auditing.AdminAuditEvent>(e =>
        {
            e.ToTable("AdminAuditEvents"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.DetailsJson).HasMaxLength(2000).IsRequired();
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.EntityType, x.EntityKey, x.CreatedAtUtc });
        });
        b.Entity<Campaign>(e =>
        {
            e.ToTable("Campaigns", t => { t.HasCheckConstraint("CK_Campaigns_Dates", "EndsAtUtc > StartsAtUtc"); t.HasCheckConstraint("CK_Campaigns_Discount", "DiscountValue >= 0 AND (DiscountType <> 1 OR DiscountValue <= 100)"); }); e.HasKey(x=>x.Id);
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
            e.ToTable("Coupons", t => { t.HasCheckConstraint("CK_Coupons_Discount", "DiscountValue >= 0 AND (DiscountType <> 1 OR DiscountValue <= 100)"); t.HasCheckConstraint("CK_Coupons_Limits", "(MaxDiscountAmountIRR IS NULL OR MaxDiscountAmountIRR >= 0) AND (MinimumPurchaseIRR IS NULL OR MinimumPurchaseIRR >= 0) AND (MaxUses IS NULL OR MaxUses > 0) AND (EndsAtUtc IS NULL OR StartsAtUtc IS NULL OR EndsAtUtc > StartsAtUtc)"); }); e.HasKey(x=>x.Id);
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
            e.ToTable("CouponUsages", t => t.HasCheckConstraint("CK_CouponUsages_Discount", "DiscountAmountIRR >= 0")); e.HasKey(x=>x.Id);
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

        // Marketplace uses application-generated long IDs for business entities. The SQL
        // bootstrap script intentionally defines these PKs without IDENTITY. Keep EF's model
        // aligned so inserts do not target identity columns that do not exist in production.
        // The three reconciliation audit tables are the only IDENTITY-backed long keys.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var idProperty = entityType.FindProperty("Id");
            if (idProperty?.ClrType == typeof(long) &&
                entityType.ClrType.Name is not nameof(PaymentReconciliationAudit)
                    and not nameof(RefundReconciliationAudit)
                    and not nameof(SettlementReconciliationAudit)
                    and not nameof(Marketplace.Domain.Auditing.AdminAuditEvent))
            {
                idProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
