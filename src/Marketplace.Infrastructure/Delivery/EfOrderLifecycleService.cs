using Marketplace.Application.Delivery.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Delivery;

public sealed class EfOrderLifecycleService(MarketplaceDbContext db, ICurrentUser currentUser, IInventoryReservationService inventory, ISellerBalanceService sellerBalance, IClock clock, IIdGenerator ids) : IOrderLifecycleService
{
    public async Task StartPreparingAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await EnsureSellerOrAdminAsync(orderId, cancellationToken);
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.StartPreparing();
        await db.SaveChangesAsync(cancellationToken);
    }



    public async Task<DeliveryCodeResult> MarkReadyAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await EnsureSellerOrAdminAsync(orderId, cancellationToken);
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

    public async Task ConfirmAsync(long orderId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Delivery code is required.", nameof(code));

        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");
        if (order.CustomerId != currentUser.UserId && !await IsAdminAsync(cancellationToken))
            throw new UnauthorizedAccessException("Only the customer or admin can confirm delivery.");

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var deliveryCode = await db.DeliveryCodes.SingleAsync(
                x => x.OrderId == orderId, cancellationToken);

            if (!deliveryCode.Matches(code))
                throw new InvalidOperationException("Invalid or expired delivery code.");

            order.MarkDelivered();
            deliveryCode.MarkUsed();

            await inventory.ConsumeAsync(orderId, cancellationToken);

            var commission = await db.Commissions.SingleAsync(
                x => x.OrderId == orderId, cancellationToken);

            await sellerBalance.ReleasePendingAsync(
                commission.SellerId,
                orderId,
                commission.SellerAmountIRR,
                $"DELIVERY:{orderId}",
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ExpireAsync(long orderId, CancellationToken cancellationToken = default)
    {
        await EnsureSellerOrAdminAsync(orderId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
            order.MarkDeliveryExpired();
            var code = await db.DeliveryCodes.SingleAsync(x => x.OrderId == orderId, cancellationToken);
            code.Expire();
            await inventory.ReleaseAsync(orderId, true, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CompleteAsync(long orderId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || (await db.Orders.Where(x => x.Id == orderId).Select(x => (long?)x.CustomerId).SingleOrDefaultAsync(cancellationToken) != currentUser.UserId && !await IsAdminAsync(cancellationToken)))
            throw new UnauthorizedAccessException("Only the customer or admin can complete the order.");
        var order = await db.Orders.SingleAsync(x => x.Id == orderId, cancellationToken);
        order.Complete();
        await db.SaveChangesAsync(cancellationToken);
    }
    private async Task EnsureSellerOrAdminAsync(long orderId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");
        if (await IsAdminAsync(cancellationToken))
            return;

        var storeId = await db.Orders.Where(x => x.Id == orderId)
            .Select(x => x.StoreId)
            .SingleAsync(cancellationToken);
        var ownerUserId = await (from s in db.Stores
                                 join seller in db.Sellers on s.SellerId equals seller.Id
                                 where s.Id == storeId
                                 select seller.UserId)
            .SingleAsync(cancellationToken);

        if (ownerUserId != currentUser.UserId)
            throw new UnauthorizedAccessException("Only the store seller or admin can change the order.");
    }

    private Task<bool> IsAdminAsync(CancellationToken cancellationToken)
        => db.UserUserTypes.AnyAsync(
            x => x.UserId == currentUser.UserId &&
                 x.UserTypeId == UserTypeId.Admin,
            cancellationToken);
}
