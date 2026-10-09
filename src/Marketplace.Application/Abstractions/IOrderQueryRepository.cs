using Marketplace.Domain.Orders;
namespace Marketplace.Application.Abstractions;
public sealed record OrderSummary(long Id,long StoreId,long SellerId,long CustomerId,OrderStatus Status,long SubtotalIRR,long CampaignDiscountIRR,long CouponDiscountIRR,long TotalIRR,string? CouponCode,DateTime CreatedAtUtc,long ShippingFeeIRR=0);
public sealed record OrderRefundSummary(long Id,long AmountIRR,string Status,string Reason,DateTime RequestedAtUtc,DateTime? CompletedAtUtc,string? ProviderReference);
public sealed record OrderDeliveryAddressSnapshot(string? RecipientName,string? RecipientMobile,string? AddressLine,string? PostalCode,string? DeliveryNote,string? CityName,string? ProvinceName);
public sealed record OrderDetails(OrderSummary Order,IReadOnlyList<OrderItemSnapshot> Items,string? PaymentStatus,string? PaymentReference,DateTime? DeliveredAtUtc,DateTime? ComplaintExpiresAtUtc,OrderRefundSummary? Refund=null,OrderDeliveryAddressSnapshot? DeliveryAddress=null);
public sealed record OrderItemSnapshot(long ProductId,long? VariantId,string ProductName,int Quantity,long BaseUnitPriceIRR,long CampaignDiscountIRR,long CouponDiscountIRR,long WarrantyPriceIRR,long LineTotalIRR);
public interface IOrderQueryRepository
{
 Task<Order?> GetAsync(long orderId,CancellationToken ct=default);
 Task<List<Order>> GetCustomerOrdersAsync(long customerId,CancellationToken ct=default);
 Task<List<Order>> GetSellerOrdersAsync(long sellerId,CancellationToken ct=default);
 Task<List<OrderItem>> GetItemsAsync(long orderId,CancellationToken ct=default);
 Task<Marketplace.Domain.Payments.Payment?> GetPaymentAsync(long orderId,CancellationToken ct=default);
 Task<Marketplace.Domain.Refunds.Refund?> GetRefundByOrderAsync(long orderId,CancellationToken ct=default);
}