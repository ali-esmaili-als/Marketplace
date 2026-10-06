using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Inventory;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Checkout;

public sealed class EfInventoryReservationService(MarketplaceDbContext db, IIdGenerator ids, IClock clock)
    : IInventoryReservationService
{
    public async Task ReserveAsync(long orderId, IReadOnlyList<InventoryReservationRequest> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items.OrderBy(x => x.ProductVariantId))
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.ProductVariants
                SET ReservedQuantity = ReservedQuantity + {item.Quantity}
                WHERE Id = {item.ProductVariantId}
                  AND IsActive = 1
                  AND StockQuantity - ReservedQuantity >= {item.Quantity}
                """, cancellationToken);

            if (affected != 1)
                throw new InvalidOperationException($"Insufficient stock for variant {item.ProductVariantId}.");

            db.InventoryReservations.Add(InventoryReservation.Create(
                ids.NewId(), orderId, item.ProductId, item.ProductVariantId,
                item.Quantity, clock.UtcNow.AddMinutes(20)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

    public async Task ConsumeAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var reservations = await db.InventoryReservations
            .Where(x => x.OrderId == orderId && x.Status == InventoryReservationStatus.Reserved)
            .OrderBy(x => x.ProductVariantId)
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
            throw new InvalidOperationException("No active inventory reservations found for order.");

        foreach (var reservation in reservations)
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.ProductVariants
                SET ReservedQuantity = ReservedQuantity - {reservation.Quantity},
                    StockQuantity = StockQuantity - {reservation.Quantity}
                WHERE Id = {reservation.ProductVariantId}
                  AND ReservedQuantity >= {reservation.Quantity}
                  AND StockQuantity >= {reservation.Quantity}
                """, cancellationToken);

            if (affected != 1)
                throw new InvalidOperationException(
                    $"Inventory consumption failed for variant {reservation.ProductVariantId}.");

            reservation.Consume();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
