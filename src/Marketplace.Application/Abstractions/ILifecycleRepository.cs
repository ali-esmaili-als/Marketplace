using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Abstractions;

public interface ILifecycleRepository
{
    Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default);
    Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default);
    Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default);
    Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default);
    void AddDelivery(Delivery delivery);
    void AddComplaint(Complaint complaint);
    void AddRefund(Refund refund);
    void AddBalanceTransaction(BalanceTransaction transaction);
}