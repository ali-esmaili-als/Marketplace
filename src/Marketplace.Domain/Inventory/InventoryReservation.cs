using Marketplace.Domain.Common;

namespace Marketplace.Domain.Inventory;

public sealed class InventoryReservation : AggregateRoot<long>
{
    private InventoryReservation() { }
    public long OrderId { get; private set; }
    public long ProductId { get; private set; }
    public long ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public InventoryReservationStatus Status { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static InventoryReservation Create(long id,long orderId,long productId,long productVariantId,int quantity,DateTime? expiresAtUtc)
    {
        if(quantity<=0) throw new DomainException("Reservation quantity must be positive.");
        if(expiresAtUtc.HasValue && expiresAtUtc<=DateTime.UtcNow) throw new DomainException("Reservation expiry must be in the future.");
        return new InventoryReservation { Id=id, OrderId=orderId, ProductId=productId, ProductVariantId=productVariantId, Quantity=quantity, Status=InventoryReservationStatus.Reserved, ReservedAtUtc=DateTime.UtcNow, ExpiresAtUtc=expiresAtUtc };
    }
    public void Consume(){Require(InventoryReservationStatus.Reserved);Status=InventoryReservationStatus.Consumed;CompletedAtUtc=DateTime.UtcNow;}
    public void Release(){Require(InventoryReservationStatus.Reserved);Status=InventoryReservationStatus.Released;CompletedAtUtc=DateTime.UtcNow;}
    public void Expire(){Require(InventoryReservationStatus.Reserved);Status=InventoryReservationStatus.Expired;CompletedAtUtc=DateTime.UtcNow;}
    private void Require(InventoryReservationStatus status){if(Status!=status)throw new DomainException($"Reservation must be in {status} status.");}
}
