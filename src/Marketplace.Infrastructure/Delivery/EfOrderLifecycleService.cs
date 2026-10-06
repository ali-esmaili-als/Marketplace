using Marketplace.Application.Delivery.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Delivery;

public sealed class EfOrderLifecycleService(MarketplaceDbContext db, ICurrentUser currentUser, IInventoryReservationService inventory, IClock clock, IIdGenerator ids) : IOrderLifecycleService
{
    public async Task StartPreparingAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.StartPreparing();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DeliveryCodeResult> MarkReadyAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.MarkReadyForDelivery();

        if (await db.DeliveryCodes.AnyAsync(x => x.OrderId == orderId, cancellationToken))
            throw new InvalidOperationException("Delivery code already exists.");

        var rawCode = Random.Shared.Next(100000, 999999).ToString();
        var expiresAtUtc = clock.UtcNow.AddDays(3);
        db.DeliveryCodes.Add(Marketplace.Domain.Delivery.DeliveryCode.Create(
            ids.NewId(), orderId, rawCode, expiresAtUtc));

        await db.SaveChangesAsync(cancellationToken);
        return new DeliveryCodeResult(orderId, rawCode, expiresAtUtc);
    }

    public async Task ExpireAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.MarkDeliveryExpired();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.Complete();
        await db.SaveChangesAsync(cancellationToken);
    }
}