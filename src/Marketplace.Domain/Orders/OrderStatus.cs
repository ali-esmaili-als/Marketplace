namespace Marketplace.Domain.Orders;

public enum OrderStatus : byte
{
    PendingPayment=1, Paid=2, Preparing=3, ReadyForDelivery=4,
    Delivered=5, DeliveryExpired=6, RefundRequested=7, Refunded=8,
    Completed=9, Cancelled=10
}