using Microsoft.EntityFrameworkCore;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Sellers;

namespace Marketplace.Infrastructure.Persistence;

public sealed class MarketplaceDbContext:DbContext
{
    public MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options):base(options){}
    public DbSet<Order> Orders=>Set<Order>();
    public DbSet<OrderItem> OrderItems=>Set<OrderItem>();
    public DbSet<Payment> Payments=>Set<Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions=>Set<PaymentTransaction>();
    public DbSet<Delivery> Deliveries=>Set<Delivery>();
    public DbSet<Refund> Refunds=>Set<Refund>();
    public DbSet<Complaint> Complaints=>Set<Complaint>();
    public DbSet<InventoryItem> InventoryItems=>Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations=>Set<InventoryReservation>();
    public DbSet<SellerBalance> SellerBalances=>Set<SellerBalance>();
    public DbSet<SellerBalanceHold> SellerBalanceHolds=>Set<SellerBalanceHold>();
    public DbSet<BalanceTransaction> BalanceTransactions=>Set<BalanceTransaction>();
    public DbSet<Commission> Commissions=>Set<Commission>();
    public DbSet<CommissionReversal> CommissionReversals=>Set<CommissionReversal>();
    public DbSet<Seller> Sellers=>Set<Seller>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>(e=>{e.ToTable("Orders");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.HasIndex(x=>new{x.SellerId,x.Status});e.HasIndex(x=>new{x.CustomerId,x.CreatedAtUtc});});
        b.Entity<OrderItem>(e=>{e.ToTable("OrderItems");e.HasKey(x=>x.Id);e.Property(x=>x.ProductNameSnapshot).HasMaxLength(300).IsRequired();e.Property(x=>x.VariantSnapshot).HasMaxLength(1000);e.Property(x=>x.WarrantySnapshot).HasMaxLength(500);e.HasIndex(x=>x.OrderId);});
        b.Entity<Payment>(e=>{e.ToTable("Payments");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.Provider).HasMaxLength(100);e.Property(x=>x.Authority).HasMaxLength(200);e.Property(x=>x.ReferenceNumber).HasMaxLength(200);e.HasIndex(x=>x.OrderId).IsUnique();e.HasIndex(x=>x.Authority);});
        b.Entity<PaymentTransaction>(e=>{e.ToTable("PaymentTransactions");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.Provider).HasMaxLength(100).IsRequired();e.Property(x=>x.Authority).HasMaxLength(200);e.Property(x=>x.Reference).HasMaxLength(200);e.HasIndex(x=>new{x.PaymentId,x.Status});});
        b.Entity<Delivery>(e=>{e.ToTable("Deliveries");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.ConfirmationReference).HasMaxLength(200);e.HasIndex(x=>x.OrderId).IsUnique();e.HasIndex(x=>new{x.Status,x.ExpiresAtUtc});});
        b.Entity<Refund>(e=>{e.ToTable("Refunds");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.Reason).HasConversion<byte>();e.Property(x=>x.ProviderReference).HasMaxLength(200);e.Property(x=>x.FailureReason).HasMaxLength(1000);e.HasIndex(x=>new{x.OrderId,x.Status});});
        b.Entity<Complaint>(e=>{e.ToTable("Complaints");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.Reason).HasMaxLength(2000).IsRequired();e.Property(x=>x.ResolutionNote).HasMaxLength(4000);e.HasIndex(x=>new{x.OrderId,x.Status});});
        b.Entity<InventoryItem>(e=>{e.ToTable("InventoryItems");e.HasKey(x=>x.Id);e.HasIndex(x=>x.ProductVariantId).IsUnique();});
        b.Entity<InventoryReservation>(e=>{e.ToTable("InventoryReservations");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.HasIndex(x=>new{x.OrderId,x.Status});e.HasIndex(x=>new{x.Status,x.ExpiresAtUtc});});
        b.Entity<SellerBalance>(e=>{e.ToTable("SellerBalances");e.HasKey(x=>x.Id);e.HasIndex(x=>x.SellerId).IsUnique();});
        b.Entity<SellerBalanceHold>(e=>{e.ToTable("SellerBalanceHolds");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.Property(x=>x.Reason).HasMaxLength(500).IsRequired();e.HasIndex(x=>new{x.OrderId,x.Status});});
        b.Entity<BalanceTransaction>(e=>{e.ToTable("BalanceTransactions");e.HasKey(x=>x.Id);e.Property(x=>x.Type).HasConversion<byte>();e.Property(x=>x.Reference).HasMaxLength(200);e.HasIndex(x=>new{x.SellerId,x.CreatedAtUtc});e.HasIndex(x=>new{x.OrderId,x.Type});});
        b.Entity<Commission>(e=>{e.ToTable("Commissions");e.HasKey(x=>x.Id);e.Property(x=>x.CommissionRate).HasPrecision(9,4);e.HasIndex(x=>x.OrderId).IsUnique();});
        b.Entity<CommissionReversal>(e=>{e.ToTable("CommissionReversals");e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.CommissionId,x.RefundId}).IsUnique();});
        b.Entity<Seller>(e=>{e.ToTable("Sellers");e.HasKey(x=>x.Id);e.Property(x=>x.Status).HasConversion<byte>();e.HasIndex(x=>x.UserId).IsUnique();});
    }
}