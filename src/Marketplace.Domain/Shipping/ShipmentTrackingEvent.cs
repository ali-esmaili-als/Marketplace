using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class ShipmentTrackingEvent : Entity<long>
{
    private ShipmentTrackingEvent() { }

    public long ShipmentId { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public string Description { get; private set; } = null!;
    public string? Location { get; private set; }
    public long ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static ShipmentTrackingEvent Create(long id, long shipmentId, ShipmentStatus status, string description, string? location, long actorUserId, DateTime occurredAtUtc)
    {
        if (id <= 0 || shipmentId <= 0 || actorUserId <= 0) throw new DomainException("Invalid shipment tracking event identifiers.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1000) throw new DomainException("Tracking event description is required and must be at most 1000 characters.");
        if (location?.Trim().Length > 200) throw new DomainException("Tracking event location must be at most 200 characters.");
        if (occurredAtUtc > DateTime.UtcNow.AddMinutes(5)) throw new DomainException("Tracking event time cannot be in the future.");
        return new ShipmentTrackingEvent { Id = id, ShipmentId = shipmentId, Status = status, Description = description.Trim(), Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(), ActorUserId = actorUserId, OccurredAtUtc = occurredAtUtc, CreatedAtUtc = DateTime.UtcNow };
    }
}
