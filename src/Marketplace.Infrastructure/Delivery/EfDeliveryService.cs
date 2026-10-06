using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Delivery.Ports;
using Marketplace.Domain.Orders;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Delivery;

public sealed class EfDeliveryService(MarketplaceDbContext db, IClock clock) : IDeliveryService
{
    public async Task ConfirmAsync(long orderId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Delivery code is required.", nameof(code));

        var delivery = await db.DeliveryCodes.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Delivery code not found.");

        if (!delivery.Matches(code))
            throw new InvalidOperationException("Invalid or expired delivery code.");

        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            delivery.MarkUsed();
            order.MarkDelivered();
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}