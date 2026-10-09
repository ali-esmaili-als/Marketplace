using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class Shipment : AggregateRoot<long>
{
    private Shipment() { }

    public long OrderId { get; private set; }
    public long SellerId { get; private set; }
    public string CarrierName { get; private set; } = null!;
    public string TrackingNumber { get; private set; } = null!;
    public string? TrackingUrl { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? CarrierDeliveredAtUtc { get; private set; }

    public static Shipment Create(long id, long orderId, long sellerId, string carrierName, string trackingNumber, string? trackingUrl, DateTime now)
    {
        if (id <= 0 || orderId <= 0 || sellerId <= 0) throw new DomainException("Invalid shipment identifiers.");
        if (string.IsNullOrWhiteSpace(carrierName) || carrierName.Trim().Length > 150) throw new DomainException("Carrier name is required and must be at most 150 characters.");
        if (string.IsNullOrWhiteSpace(trackingNumber) || trackingNumber.Trim().Length > 150) throw new DomainException("Tracking number is required and must be at most 150 characters.");
        ValidateTrackingUrl(trackingUrl);
        return new Shipment { Id = id, OrderId = orderId, SellerId = sellerId, CarrierName = carrierName.Trim(), TrackingNumber = trackingNumber.Trim(), TrackingUrl = string.IsNullOrWhiteSpace(trackingUrl) ? null : trackingUrl.Trim(), Status = ShipmentStatus.Registered, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    public void ChangeStatus(ShipmentStatus next, DateTime now)
    {
        var allowed = Status switch
        {
            ShipmentStatus.Registered => next is ShipmentStatus.Shipped or ShipmentStatus.Cancelled,
            ShipmentStatus.Shipped => next is ShipmentStatus.InTransit or ShipmentStatus.OutForDelivery or ShipmentStatus.Exception or ShipmentStatus.Returned,
            ShipmentStatus.InTransit => next is ShipmentStatus.OutForDelivery or ShipmentStatus.Exception or ShipmentStatus.Returned,
            ShipmentStatus.OutForDelivery => next is ShipmentStatus.CarrierDelivered or ShipmentStatus.Exception or ShipmentStatus.Returned,
            ShipmentStatus.Exception => next is ShipmentStatus.InTransit or ShipmentStatus.OutForDelivery or ShipmentStatus.Returned,
            _ => false
        };
        if (!allowed) throw new DomainException($"Shipment cannot transition from {Status} to {next}.");
        if (now < UpdatedAtUtc) throw new DomainException("Shipment update time cannot move backwards.");
        Status = next;
        UpdatedAtUtc = now;
        if (next == ShipmentStatus.Shipped) ShippedAtUtc ??= now;
        if (next == ShipmentStatus.CarrierDelivered) CarrierDeliveredAtUtc = now;
    }

    private static void ValidateTrackingUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Trim().Length > 1000 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new DomainException("Tracking URL must be a valid HTTP or HTTPS URL.");
    }
}
