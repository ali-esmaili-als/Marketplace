namespace Marketplace.Domain.Inventory;

public enum InventoryReservationStatus : byte
{
    Reserved = 1,
    Consumed = 2,
    Released = 3,
    Expired = 4
}
