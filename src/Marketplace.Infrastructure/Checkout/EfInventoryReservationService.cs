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