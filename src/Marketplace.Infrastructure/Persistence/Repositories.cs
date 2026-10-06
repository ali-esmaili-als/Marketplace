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

public sealed class LifecycleRepository(MarketplaceDbContext db) : ILifecycleRepository
{
    public Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default)=>db.Deliveries.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<Complaint?> GetComplaintAsync(long complaintId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.Id==complaintId,ct);
    public Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=ComplaintStatus.Closed&&x.Status!=ComplaintStatus.Cancelled,ct);
    public Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=RefundStatus.Completed&&x.Status!=RefundStatus.Rejected,ct);
    public Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default)=>db.SellerBalances.SingleOrDefaultAsync(x=>x.SellerId==sellerId,ct);
    public Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default)=>db.SellerBalanceHolds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status==BalanceHoldStatus.Active,ct);
    public Task<Commission?> GetCommissionByOrderAsync(long orderId,CancellationToken ct=default)=>db.Commissions.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default)=>db.InventoryReservations.Where(x=>x.OrderId==orderId).ToListAsync(ct);
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
