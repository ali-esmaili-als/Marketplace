namespace Marketplace.Domain.Shipping;

public enum ShipmentStatus : byte
{
    Registered = 1,
    Shipped = 2,
    InTransit = 3,
    OutForDelivery = 4,
    CarrierDelivered = 5,
    Exception = 6,
    Returned = 7,
    Cancelled = 8
}
