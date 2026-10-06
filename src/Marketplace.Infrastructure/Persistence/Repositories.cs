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

namespace Marketplace.Infrastructure.Persistence;

public sealed class OrderRepository(MarketplaceDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(long id, CancellationToken ct=default) => db.Orders.SingleOrDefaultAsync(x=>x.Id==id,ct);
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
    public Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default)=>db.Deliveries.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<Complaint?> GetComplaintAsync(long complaintId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.Id==complaintId,ct);
    public Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=ComplaintStatus.Closed&&x.Status!=ComplaintStatus.Cancelled,ct);
    public Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=RefundStatus.Completed&&x.Status!=RefundStatus.Rejected&&x.Status!=RefundStatus.Failed,ct);
    public Task<Refund?> GetRefundAsync(long refundId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.Id==refundId,ct);
    public Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default)=>db.SellerBalances.SingleOrDefaultAsync(x=>x.SellerId==sellerId,ct);
    public Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default)=>db.SellerBalanceHolds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status==BalanceHoldStatus.Active,ct);
    public Task<Commission?> GetCommissionByOrderAsync(long orderId,CancellationToken ct=default)=>db.Commissions.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default)=>db.InventoryReservations.Where(x=>x.OrderId==orderId).ToListAsync(ct);
    public Task<InventoryItem?> GetInventoryItemAsync(long productVariantId,CancellationToken ct=default)=>db.InventoryItems.SingleOrDefaultAsync(x=>x.ProductVariantId==productVariantId,ct);
    public Task<SellerBankAccount?> GetSellerBankAccountAsync(long sellerId,long bankAccountId,CancellationToken ct=default)
        => db.SellerBankAccounts.SingleOrDefaultAsync(x=>x.Id==bankAccountId&&x.SellerId==sellerId,ct);
    public Task<Settlement?> GetSettlementAsync(long settlementId,CancellationToken ct=default)
        => db.Settlements.SingleOrDefaultAsync(x=>x.Id==settlementId,ct);
    public void AddSettlement(Settlement settlement)=>db.Settlements.Add(settlement);
    public void AddDelivery(Delivery delivery)=>db.Deliveries.Add(delivery);
    public void AddComplaint(Complaint complaint)=>db.Complaints.Add(complaint);
    public void AddRefund(Refund refund)=>db.Refunds.Add(refund);
    public void AddBalanceHold(SellerBalanceHold hold)=>db.SellerBalanceHolds.Add(hold);
    public void AddBalanceTransaction(BalanceTransaction transaction)=>db.BalanceTransactions.Add(transaction);
    public void AddCommissionReversal(CommissionReversal reversal)=>db.CommissionReversals.Add(reversal);
    public void AddOrderItem(OrderItem item)=>db.OrderItems.Add(item);
    public void AddInventoryReservation(InventoryReservation reservation)=>db.InventoryReservations.Add(reservation);
    public void AddCommission(Commission commission)=>db.Commissions.Add(commission);
}
