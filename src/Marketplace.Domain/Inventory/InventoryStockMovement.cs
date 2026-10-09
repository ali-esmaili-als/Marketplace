using Marketplace.Domain.Common;

namespace Marketplace.Domain.Inventory;

/// <summary>Immutable audit record for a seller-initiated stock adjustment.</summary>
public sealed class InventoryStockMovement : Entity<long>
{
    private InventoryStockMovement() { }

    public long ProductVariantId { get; private set; }
    public long SellerId { get; private set; }
    public long PreviousStockQuantity { get; private set; }
    public long NewStockQuantity { get; private set; }
    public long QuantityDelta => NewStockQuantity - PreviousStockQuantity;
    public string Reason { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static InventoryStockMovement Create(long id, long productVariantId, long sellerId,
        long previousStockQuantity, long newStockQuantity, string reason)
    {
        if (id <= 0 || productVariantId <= 0 || sellerId <= 0 ||
            previousStockQuantity < 0 || newStockQuantity < 0 ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new DomainException("Invalid inventory stock movement.");

        return new InventoryStockMovement
        {
            Id = id,
            ProductVariantId = productVariantId,
            SellerId = sellerId,
            PreviousStockQuantity = previousStockQuantity,
            NewStockQuantity = newStockQuantity,
            Reason = reason.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
