using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
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
using Marketplace.Domain.Pricing;
using Marketplace.Domain.Notifications;

namespace Marketplace.Infrastructure.Persistence;

public sealed class OrderRepository(MarketplaceDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(long id, CancellationToken ct=default) => db.Orders.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Order?> GetByCustomerRequestKeyAsync(long customerId,string requestKey,CancellationToken ct=default) => db.Orders.SingleOrDefaultAsync(x=>x.CustomerId==customerId&&x.RequestKey==requestKey,ct);
    public void Add(Order order) => db.Orders.Add(order);
}

public sealed class PaymentRepository(MarketplaceDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetAsync(long id, CancellationToken ct=default) => db.Payments.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Payment?> GetByOrderAsync(long orderId, CancellationToken ct=default) => db.Payments.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<PaymentTransaction?> GetLatestTransactionAsync(long paymentId, CancellationToken ct=default)
        => db.PaymentTransactions.Where(x=>x.PaymentId==paymentId).OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
    public void AddTransaction(PaymentTransaction transaction) => db.PaymentTransactions.Add(transaction);
    public void Add(Payment payment) => db.Payments.Add(payment);
}

public sealed class CartRepository(MarketplaceDbContext db) : ICartRepository
{
    public Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct=default)
        => db.Carts.SingleOrDefaultAsync(x=>x.CustomerId==customerId,ct);

    public Task<List<CartItem>> GetItemsAsync(long cartId, CancellationToken ct=default)
        => db.CartItems.Where(x=>x.CartId==cartId).OrderBy(x=>x.Id).ToListAsync(ct);

    public Task<CartItem?> GetItemAsync(long cartId, long variantId, long? warrantyId, CancellationToken ct=default)
        => db.CartItems.SingleOrDefaultAsync(x=>x.CartId==cartId && x.ProductVariantId==variantId && x.WarrantyId==warrantyId,ct);

    public void Add(Cart cart) => db.Carts.Add(cart);
    public void AddItem(CartItem item) => db.CartItems.Add(item);
    public void RemoveItem(CartItem item) => db.CartItems.Remove(item);
}

public sealed class CatalogRepository(MarketplaceDbContext db) : ICatalogRepository
{
    public async Task<CheckoutLineData?> GetCheckoutLineAsync(long variantId, long? warrantyId, CancellationToken ct=default)
    {
        var variant=await db.ProductVariants.SingleOrDefaultAsync(x=>x.Id==variantId,ct);
        if(variant is null) return null;

        var product=await db.Products.SingleOrDefaultAsync(x=>x.Id==variant.ProductId,ct);
        if(product is null) return null;

        var inventory=await db.InventoryItems.SingleOrDefaultAsync(x=>x.ProductVariantId==variantId,ct);
        if(inventory is null) return null;

        Warranty? warranty=null;
        if(warrantyId.HasValue)
        {
            warranty=await db.Warranties.SingleOrDefaultAsync(x=>x.Id==warrantyId.Value,ct);
            if(warranty is null) return null;
            var linked=await db.ProductWarranties.AnyAsync(x=>x.ProductId==product.Id && x.WarrantyId==warranty.Id && x.IsActive,ct);
            if(!linked) return null;
        }

        return new CheckoutLineData(product,variant,inventory,warranty);
    }

    public Task<Store?> GetStoreAsync(long storeId, CancellationToken ct=default)
        => db.Stores.SingleOrDefaultAsync(x=>x.Id==storeId,ct);
}


public sealed class SellerManagementRepository(MarketplaceDbContext db) : ISellerManagementRepository
{
    public Task<Seller?> GetSellerByUserIdAsync(long userId, CancellationToken ct=default)
        => db.Sellers.SingleOrDefaultAsync(x=>x.UserId==userId,ct);
    public Task<Seller?> GetSellerAsync(long sellerId, CancellationToken ct=default)
        => db.Sellers.SingleOrDefaultAsync(x=>x.Id==sellerId,ct);
    public Task<int> GetStoreCountAsync(long sellerId, CancellationToken ct=default)
        => db.Stores.CountAsync(x=>x.SellerId==sellerId && x.Status!=StoreStatus.Closed,ct);
    public Task<Store?> GetStoreAsync(long storeId, CancellationToken ct=default)
        => db.Stores.SingleOrDefaultAsync(x=>x.Id==storeId,ct);
    public Task<List<Store>> GetStoresAsync(long sellerId, CancellationToken ct=default)
        => db.Stores.Where(x=>x.SellerId==sellerId).OrderBy(x=>x.Id).ToListAsync(ct);
    public Task<Store?> GetStoreForSellerAsync(long storeId,long sellerId,CancellationToken ct=default)
        => db.Stores.SingleOrDefaultAsync(x=>x.Id==storeId && x.SellerId==sellerId,ct);
    public Task<SellerBankAccount?> GetBankAccountAsync(long sellerId,long accountId,CancellationToken ct=default)
        => db.SellerBankAccounts.SingleOrDefaultAsync(x=>x.Id==accountId && x.SellerId==sellerId,ct);
    public Task<List<SellerBankAccount>> GetBankAccountsAsync(long sellerId,CancellationToken ct=default)
        => db.SellerBankAccounts.Where(x=>x.SellerId==sellerId).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Id).ToListAsync(ct);
    public Task<SellerBalance?> GetBalanceAsync(long sellerId,CancellationToken ct=default)
        => db.SellerBalances.SingleOrDefaultAsync(x=>x.SellerId==sellerId,ct);
    public Task<bool> StoreBelongsToSellerAsync(long storeId,long sellerId,CancellationToken ct=default)
        => db.Stores.AnyAsync(x=>x.Id==storeId && x.SellerId==sellerId,ct);
    public Task<bool> StoreSlugExistsAsync(long sellerId,string slug,long? exceptStoreId=null,CancellationToken ct=default)
        => db.Stores.AnyAsync(x=>x.SellerId==sellerId && x.Slug==slug.Trim() && (!exceptStoreId.HasValue || x.Id!=exceptStoreId.Value),ct);
    public void AddSeller(Seller seller)=>db.Sellers.Add(seller);
    public void AddStore(Store store)=>db.Stores.Add(store);
    public void AddBankAccount(SellerBankAccount account)=>db.SellerBankAccounts.Add(account);
    public void AddBalance(SellerBalance balance)=>db.SellerBalances.Add(balance);
}

public sealed class ShippingRepository(MarketplaceDbContext db) : IShippingRepository
{
    public Task<DeliveryCity?> GetCityAsync(long cityId, CancellationToken ct=default)
        => db.DeliveryCities.SingleOrDefaultAsync(x => x.Id == cityId, ct);

    public Task<List<DeliveryCity>> GetActiveCitiesAsync(CancellationToken ct=default)
        => db.DeliveryCities.Where(x => x.IsActive).OrderBy(x => x.ProvinceName).ThenBy(x => x.Name).ToListAsync(ct);

    public Task<List<DeliveryCity>> GetStoreCitiesAsync(long storeId, CancellationToken ct=default)
        => db.StoreShippingCities
            .Where(x => x.StoreId == storeId && x.City.IsActive)
            .Select(x => x.City)
            .OrderBy(x => x.ProvinceName).ThenBy(x => x.Name)
            .ToListAsync(ct);

    public Task<bool> StoreShipsToCityAsync(long storeId, long cityId, CancellationToken ct=default)
        => db.StoreShippingCities.AnyAsync(x => x.StoreId == storeId && x.CityId == cityId && x.City.IsActive, ct);

    public Task<List<long>> GetStoreCityIdsAsync(long storeId, CancellationToken ct=default)
        => db.StoreShippingCities.Where(x => x.StoreId == storeId).Select(x => x.CityId).ToListAsync(ct);

    public async Task ReplaceStoreCitiesAsync(long storeId, IReadOnlyCollection<long> cityIds, CancellationToken ct=default)
    {
        var desired = cityIds.Distinct().ToHashSet();
        var current = await db.StoreShippingCities.Where(x => x.StoreId == storeId).ToListAsync(ct);

        foreach (var row in current.Where(x => !desired.Contains(x.CityId)))
            db.StoreShippingCities.Remove(row);

        var currentIds = current.Select(x => x.CityId).ToHashSet();
        foreach (var cityId in desired.Where(x => !currentIds.Contains(x)))
        {
            var id = await db.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo.MarketplaceSequence").SingleAsync(ct);
            db.StoreShippingCities.Add(StoreShippingCity.Create(id, storeId, cityId));
        }
    }

    public void AddStoreShippingCity(StoreShippingCity item) => db.StoreShippingCities.Add(item);

    public void RemoveStoreShippingCities(IEnumerable<StoreShippingCity> items)
        => db.StoreShippingCities.RemoveRange(items);
}

public sealed class LifecycleRepository(MarketplaceDbContext db) : ILifecycleRepository
{
    public Task<DeliveryCode?> GetDeliveryCodeByOrderAsync(long orderId,CancellationToken ct=default)=>db.DeliveryCodes.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default)=>db.Deliveries.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<Complaint?> GetComplaintAsync(long complaintId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.Id==complaintId,ct);
    public Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=ComplaintStatus.Closed&&x.Status!=ComplaintStatus.Cancelled,ct);
    public Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=RefundStatus.Completed&&x.Status!=RefundStatus.Rejected&&x.Status!=RefundStatus.Failed,ct);
    public Task<Refund?> GetRefundAsync(long refundId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.Id==refundId,ct);
    public Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default)
        // Settlement requests mutate the same seller balance. An update lock prevents two
        // serializable transactions from both taking shared locks and deadlocking on promotion.
        => db.SellerBalances
            .FromSqlInterpolated($"SELECT * FROM dbo.SellerBalances WITH (UPDLOCK, HOLDLOCK) WHERE SellerId = {sellerId}")
            .SingleOrDefaultAsync(ct);
    public Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default)=>db.SellerBalanceHolds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status==BalanceHoldStatus.Active,ct);
    public Task<Commission?> GetCommissionByOrderAsync(long orderId,CancellationToken ct=default)=>db.Commissions.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default)=>db.InventoryReservations.Where(x=>x.OrderId==orderId).ToListAsync(ct);
    public Task<InventoryItem?> GetInventoryItemAsync(long productVariantId,CancellationToken ct=default)=>db.InventoryItems.SingleOrDefaultAsync(x=>x.ProductVariantId==productVariantId,ct);
    public Task<SellerBankAccount?> GetSellerBankAccountAsync(long sellerId,long bankAccountId,CancellationToken ct=default)
        => db.SellerBankAccounts.SingleOrDefaultAsync(x=>x.Id==bankAccountId&&x.SellerId==sellerId,ct);
    public Task<Settlement?> GetSettlementAsync(long settlementId,CancellationToken ct=default)
        => db.Settlements.SingleOrDefaultAsync(x=>x.Id==settlementId,ct);
    public Task<Settlement?> GetSettlementByRequestKeyAsync(long sellerId,string requestKey,CancellationToken ct=default)
        => db.Settlements.SingleOrDefaultAsync(x=>x.SellerId==sellerId && x.RequestKey==requestKey,ct);
    public void AddSettlement(Settlement settlement)=>db.Settlements.Add(settlement);
    public void AddOutboxMessage(OutboxMessage message)=>db.OutboxMessages.Add(message);
    public void AddSettlementReconciliationAudit(SettlementReconciliationAudit audit)=>db.SettlementReconciliationAudits.Add(audit);
    public void AddDelivery(Delivery delivery)=>db.Deliveries.Add(delivery);
    public void AddDeliveryCode(DeliveryCode code)=>db.DeliveryCodes.Add(code);
    public void AddComplaint(Complaint complaint)=>db.Complaints.Add(complaint);
    public void AddRefund(Refund refund)=>db.Refunds.Add(refund);
    public void AddRefundReconciliationAudit(RefundReconciliationAudit audit)=>db.RefundReconciliationAudits.Add(audit);
    public void AddBalanceHold(SellerBalanceHold hold)=>db.SellerBalanceHolds.Add(hold);
    public void AddBalanceTransaction(BalanceTransaction transaction)=>db.BalanceTransactions.Add(transaction);
    public void AddCommissionReversal(CommissionReversal reversal)=>db.CommissionReversals.Add(reversal);
    public void AddOrderItem(OrderItem item)=>db.OrderItems.Add(item);
    public void AddInventoryReservation(InventoryReservation reservation)=>db.InventoryReservations.Add(reservation);
    public void AddCommission(Commission commission)=>db.Commissions.Add(commission);
    public void AddCouponUsage(CouponUsage usage)=>db.CouponUsages.Add(usage);
}


public sealed class CatalogManagementRepository(MarketplaceDbContext db) : ICatalogManagementRepository
{
 public Task<Product?> GetProductAsync(long id,CancellationToken ct=default)=>db.Products.SingleOrDefaultAsync(x=>x.Id==id,ct);
 public Task<ProductVariant?> GetVariantAsync(long id,CancellationToken ct=default)=>db.ProductVariants.SingleOrDefaultAsync(x=>x.Id==id,ct);
 public Task<bool> ProductBelongsToStoreAsync(long productId,long storeId,CancellationToken ct=default)=>db.Products.AnyAsync(x=>x.Id==productId&&x.StoreId==storeId,ct);
 public Task<bool> VariantBelongsToStoreAsync(long variantId,long storeId,CancellationToken ct=default)=>db.ProductVariants.AnyAsync(x=>x.Id==variantId&&db.Products.Any(p=>p.Id==x.ProductId&&p.StoreId==storeId),ct);
 public Task<bool> CategoryIsActiveAsync(long categoryId,CancellationToken ct=default)=>db.Categories.AnyAsync(x=>x.Id==categoryId&&x.IsActive,ct);
 public Task<ProductAttribute?> GetAttributeAsync(long id,CancellationToken ct=default)=>db.ProductAttributes.SingleOrDefaultAsync(x=>x.Id==id,ct);
 public Task<ProductAttributeValue?> GetAttributeValueAsync(long id,CancellationToken ct=default)=>db.ProductAttributeValues.SingleOrDefaultAsync(x=>x.Id==id,ct);
 public Task<bool> AttributeBelongsToStoreAsync(long attributeId,long storeId,CancellationToken ct=default)=>db.ProductAttributes.AnyAsync(x=>x.Id==attributeId&&x.StoreId==storeId,ct);
 public Task<bool> AttributeValueBelongsToStoreAsync(long valueId,long storeId,CancellationToken ct=default)=>db.ProductAttributeValues.AnyAsync(x=>x.Id==valueId&&db.ProductAttributes.Any(a=>a.Id==x.ProductAttributeId&&a.StoreId==storeId),ct);
 public Task<bool> WarrantyBelongsToStoreAsync(long warrantyId,long storeId,CancellationToken ct=default)=>db.Warranties.AnyAsync(x=>x.Id==warrantyId&&x.StoreId==storeId&&x.IsActive,ct);
 public Task<InventoryItem?> GetInventoryAsync(long variantId,CancellationToken ct=default)=>db.InventoryItems.SingleOrDefaultAsync(x=>x.ProductVariantId==variantId,ct);
 public Task<List<Product>> GetProductsAsync(long storeId,CancellationToken ct=default)=>db.Products.Where(x=>x.StoreId==storeId).OrderByDescending(x=>x.Id).ToListAsync(ct);
 public Task<List<ProductVariant>> GetVariantsAsync(long productId,CancellationToken ct=default)=>db.ProductVariants.Where(x=>x.ProductId==productId).OrderBy(x=>x.Id).ToListAsync(ct);
 public Task<List<Warranty>> GetWarrantiesAsync(long storeId,CancellationToken ct=default)=>db.Warranties.Where(x=>x.StoreId==storeId).OrderBy(x=>x.Id).ToListAsync(ct);
 public void AddProduct(Product x)=>db.Products.Add(x); public void AddAttribute(ProductAttribute x)=>db.ProductAttributes.Add(x); public void AddAttributeValue(ProductAttributeValue x)=>db.ProductAttributeValues.Add(x); public void AddProductAttributeAssignment(ProductAttributeAssignment x)=>db.ProductAttributeAssignments.Add(x); public void AddVariantAttributeValue(VariantAttributeValue x)=>db.VariantAttributeValues.Add(x); public void AddVariant(ProductVariant x)=>db.ProductVariants.Add(x); public void AddInventory(InventoryItem x)=>db.InventoryItems.Add(x); public void AddWarranty(Warranty x)=>db.Warranties.Add(x); public void AddProductWarranty(ProductWarranty x)=>db.ProductWarranties.Add(x);
}

public sealed class OrderQueryRepository(MarketplaceDbContext db) : IOrderQueryRepository
{
 public Task<Order?> GetAsync(long orderId,CancellationToken ct=default)=>db.Orders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
 public Task<List<Order>> GetCustomerOrdersAsync(long customerId,CancellationToken ct=default)=>db.Orders.Where(x=>x.CustomerId==customerId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
 public Task<List<Order>> GetSellerOrdersAsync(long sellerId,CancellationToken ct=default)=>db.Orders.Where(x=>x.SellerId==sellerId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
 public Task<List<OrderItem>> GetItemsAsync(long orderId,CancellationToken ct=default)=>db.OrderItems.Where(x=>x.OrderId==orderId).OrderBy(x=>x.Id).ToListAsync(ct);
 public Task<Payment?> GetPaymentAsync(long orderId,CancellationToken ct=default)=>db.Payments.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
}

public sealed class MaintenanceRepository(MarketplaceDbContext db) : IMaintenanceRepository
{
 public Task<List<Delivery>> GetExpiredDeliveriesAsync(DateTime nowUtc,CancellationToken ct=default)=>db.Deliveries.Where(x=>x.Status==DeliveryStatus.Ready&&x.ExpiresAtUtc<=nowUtc).OrderBy(x=>x.ExpiresAtUtc).Take(100).ToListAsync(ct);
 public Task<List<InventoryReservation>> GetExpiredReservationsAsync(DateTime nowUtc,CancellationToken ct=default)=>db.InventoryReservations.Where(x=>x.Status==InventoryReservationStatus.Active&&x.ExpiresAtUtc<=nowUtc).OrderBy(x=>x.ExpiresAtUtc).Take(500).ToListAsync(ct);
 public Task<List<Order>> GetOrdersPendingRefundAsync(CancellationToken ct=default)=>db.Orders.Where(x=>x.Status==OrderStatus.RefundRequested).OrderBy(x=>x.Id).Take(100).ToListAsync(ct);
 public Task<List<Order>> GetOrdersReadyToCompleteAsync(DateTime nowUtc,CancellationToken ct=default)=>db.Orders.Where(x=>x.Status==OrderStatus.Delivered&&x.ComplaintExpiresAtUtc!=null&&x.ComplaintExpiresAtUtc<=nowUtc).OrderBy(x=>x.ComplaintExpiresAtUtc).Take(100).ToListAsync(ct);
 public Task<InventoryItem?> GetInventoryItemAsync(long variantId,CancellationToken ct=default)=>db.InventoryItems.SingleOrDefaultAsync(x=>x.ProductVariantId==variantId,ct);
 public Task<bool> HasOpenComplaintAsync(long orderId,CancellationToken ct=default)=>db.Complaints.AnyAsync(x=>x.OrderId==orderId&&x.Status!=ComplaintStatus.Closed&&x.Status!=ComplaintStatus.Cancelled,ct);
}

public sealed class NotificationRepository(MarketplaceDbContext db) : INotificationRepository
{
 public Task<List<Notification>> GetForUserAsync(long userId,int take,CancellationToken ct=default)=>db.Notifications.Where(x=>x.UserId==userId).OrderByDescending(x=>x.CreatedAtUtc).Take(take).ToListAsync(ct);
 public Task<Notification?> GetForUserAsync(long userId,long id,CancellationToken ct=default)=>db.Notifications.SingleOrDefaultAsync(x=>x.UserId==userId&&x.Id==id,ct);
 public Task<int> GetUnreadCountAsync(long userId,CancellationToken ct=default)=>db.Notifications.CountAsync(x=>x.UserId==userId&&x.ReadAtUtc==null,ct);
 public async Task<int> MarkAllReadAsync(long userId,CancellationToken ct=default){var unread=await db.Notifications.Where(x=>x.UserId==userId&&x.ReadAtUtc==null).ToListAsync(ct);foreach(var item in unread)item.MarkRead();return unread.Count;}
 public void Add(Notification notification)=>db.Notifications.Add(notification);
}

public sealed class CategoryRepository(MarketplaceDbContext db) : ICategoryRepository
{
 public Task<Category?> GetAsync(long id,CancellationToken ct=default)=>db.Categories.SingleOrDefaultAsync(x=>x.Id==id,ct);
 public Task<bool> SlugExistsAsync(long? parentId,string slug,long? exceptId=null,CancellationToken ct=default)=>db.Categories.AnyAsync(x=>x.ParentCategoryId==parentId&&x.Slug==slug.Trim()&&(!exceptId.HasValue||x.Id!=exceptId.Value),ct);
 public Task<List<Category>> GetChildrenAsync(long? parentId,CancellationToken ct=default)=>db.Categories.Where(x=>x.ParentCategoryId==parentId&&x.IsActive).OrderBy(x=>x.Name).ToListAsync(ct);
 public void Add(Category category)=>db.Categories.Add(category);
}