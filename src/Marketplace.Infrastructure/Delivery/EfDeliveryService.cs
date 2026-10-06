using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Delivery.Ports;
using Marketplace.Application.Finance.Ports;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Delivery;

public sealed class EfDeliveryService(
    MarketplaceDbContext db,
    ISellerBalanceService sellerBalance,
    IInventoryReservationService inventory) : IDeliveryService
{
    public async Task ConfirmAsync(long orderId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Delivery code is required.", nameof(code));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var delivery = await db.DeliveryCodes.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Delivery code not found.");

            if (!delivery.Matches(code))
                throw new InvalidOperationException("Invalid or expired delivery code.");

            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Order not found.");

            delivery.MarkUsed();
            order.MarkDelivered();

            await inventory.ConsumeAsync(orderId, cancellationToken);

            var commission = await db.Commissions.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Commission not found.");

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
}