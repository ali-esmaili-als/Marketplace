using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Pricing;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Abstractions;

public interface ILifecycleRepository
{
    Task<Delivery?> GetDeliveryByOrderAsync(long orderId,CancellationToken ct=default);
    Task<DeliveryCode?> GetDeliveryCodeByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Complaint?> GetComplaintAsync(long complaintId,CancellationToken ct=default);
    Task<Complaint?> GetOpenComplaintByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Refund?> GetActiveRefundByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Refund?> GetRefundAsync(long refundId,CancellationToken ct=default);
    Task<SellerBalance?> GetSellerBalanceAsync(long sellerId,CancellationToken ct=default);
    Task<SellerBalanceHold?> GetActiveHoldByOrderAsync(long orderId,CancellationToken ct=default);
    Task<Commission?> GetCommissionByOrderAsync(long orderId,CancellationToken ct=default);
    Task<List<InventoryReservation>> GetReservationsByOrderAsync(long orderId,CancellationToken ct=default);
    Task<InventoryItem?> GetInventoryItemAsync(long productVariantId,CancellationToken ct=default);
    Task<SellerBankAccount?> GetSellerBankAccountAsync(long sellerId,long bankAccountId,CancellationToken ct=default);
    Task<Settlement?> GetSettlementAsync(long settlementId,CancellationToken ct=default);
    void AddSettlement(Settlement settlement);
    void AddSettlementReconciliationAudit(SettlementReconciliationAudit audit);
    void AddDelivery(Delivery delivery);
    void AddDeliveryCode(DeliveryCode code);
    void AddComplaint(Complaint complaint);
    void AddRefund(Refund refund);
    void AddRefundReconciliationAudit(RefundReconciliationAudit audit);
    void AddBalanceHold(SellerBalanceHold hold);
    void AddBalanceTransaction(BalanceTransaction transaction);
    void AddCommissionReversal(CommissionReversal reversal);
    void AddOrderItem(OrderItem item);
    void AddInventoryReservation(InventoryReservation reservation);
    void AddCommission(Commission commission);
    void AddCouponUsage(CouponUsage usage);
}