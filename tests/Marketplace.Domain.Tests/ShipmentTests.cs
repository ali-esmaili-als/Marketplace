using Marketplace.Domain.Common;
using Marketplace.Domain.Shipping;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class ShipmentTests
{
    [Fact]
    public void Shipment_can_be_registered_and_moved_through_carrier_states()
    {
        var now = DateTime.UtcNow;
        var shipment = Shipment.Create(1, 2, 3, "پست", "TRK-123", "https://carrier.example/track/TRK-123", now);

        Assert.Equal(ShipmentStatus.Registered, shipment.Status);
        shipment.ChangeStatus(ShipmentStatus.Shipped, now.AddMinutes(1));
        shipment.ChangeStatus(ShipmentStatus.InTransit, now.AddMinutes(2));
        shipment.ChangeStatus(ShipmentStatus.OutForDelivery, now.AddMinutes(3));
        shipment.ChangeStatus(ShipmentStatus.CarrierDelivered, now.AddMinutes(4));

        Assert.Equal(ShipmentStatus.CarrierDelivered, shipment.Status);
        Assert.Equal(now.AddMinutes(4), shipment.CarrierDeliveredAtUtc);
        Assert.Equal(now.AddMinutes(1), shipment.ShippedAtUtc);
    }

    [Fact]
    public void Carrier_delivery_is_terminal_and_cannot_be_reopened()
    {
        var now = DateTime.UtcNow;
        var shipment = Shipment.Create(1, 2, 3, "Carrier", "TRK-123", null, now);
        shipment.ChangeStatus(ShipmentStatus.Shipped, now.AddMinutes(1));
        shipment.ChangeStatus(ShipmentStatus.InTransit, now.AddMinutes(2));
        shipment.ChangeStatus(ShipmentStatus.OutForDelivery, now.AddMinutes(3));
        shipment.ChangeStatus(ShipmentStatus.CarrierDelivered, now.AddMinutes(4));

        Assert.Throws<DomainException>(() => shipment.ChangeStatus(ShipmentStatus.InTransit, now.AddMinutes(5)));
    }

    [Fact]
    public void Invalid_tracking_urls_are_rejected()
    {
        Assert.Throws<DomainException>(() => Shipment.Create(1, 2, 3, "Carrier", "TRK-123", "javascript:alert(1)", DateTime.UtcNow));
    }

    [Fact]
    public void Tracking_event_requires_description_and_valid_time()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => ShipmentTrackingEvent.Create(1, 2, ShipmentStatus.Registered, " ", null, 3, now));
        Assert.Throws<DomainException>(() => ShipmentTrackingEvent.Create(1, 2, ShipmentStatus.Registered, "ثبت شد", null, 3, now.AddMinutes(10)));
    }
}
