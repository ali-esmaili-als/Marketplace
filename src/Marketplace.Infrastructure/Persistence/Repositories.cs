using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;

namespace Marketplace.Infrastructure.Persistence;

public sealed class OrderRepository(MarketplaceDbContext db):IOrderRepository
{
    public Task<Order?> GetAsync(long id,CancellationToken ct=default)=>db.Orders.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public void Add(Order order)=>db.Orders.Add(order);
}
public sealed class PaymentRepository(MarketplaceDbContext db):IPaymentRepository
{
    public Task<Payment?> GetAsync(long id,CancellationToken ct=default)=>db.Payments.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Payment?> GetByOrderAsync(long orderId,CancellationToken ct=default)=>db.Payments.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public void Add(Payment payment)=>db.Payments.Add(payment);
}
public sealed class LifecycleRepository(MarketplaceDbContext db):ILifecycleRepository
{
    public Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default)=>db.Deliveries.SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);
    public Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default)=>db.Complaints.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=ComplaintStatus.Closed&&x.Status!=ComplaintStatus.Cancelled,ct);
    public Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default)=>db.Refunds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status!=RefundStatus.Completed&&x.Status!=RefundStatus.Rejected,ct);
    public Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default)=>db.SellerBalances.SingleOrDefaultAsync(x=>x.SellerId==sellerId,ct);
    public Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default)=>db.SellerBalanceHolds.SingleOrDefaultAsync(x=>x.OrderId==orderId&&x.Status==BalanceHoldStatus.Active,ct);
    public Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default)=>db.InventoryReservations.Where(x=>x.OrderId==orderId).ToListAsync(ct);
    public void AddDelivery(Delivery delivery)=>db.Deliveries.Add(delivery);
    public void AddComplaint(Complaint complaint)=>db.Complaints.Add(complaint);
    public void AddRefund(Refund refund)=>db.Refunds.Add(refund);
    public void AddBalanceTransaction(BalanceTransaction transaction)=>db.BalanceTransactions.Add(transaction);
}