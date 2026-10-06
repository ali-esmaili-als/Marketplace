namespace Marketplace.Domain.Orders;

public enum OrderStatus : byte
{
    PendingPayment=1,
    Paid=2,
    Preparing=3,
    ReadyForDelivery=4,
    Delivered=5,
    Completed=6,
    Cancelled=7,
    DeliveryExpired=8,
    Disputed=9,
    RefundRequested=10,
    Refunded=11,
    PartiallyRefunded=12
}
