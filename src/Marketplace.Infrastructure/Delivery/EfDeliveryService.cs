using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Delivery.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Delivery;

public sealed class EfDeliveryService(
    MarketplaceDbContext db,
    IIdGenerator ids,
    IClock clock) : IDeliveryService
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

            var commission = await db.Commissions.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Commission not found.");

            var balance = await db.SellerBalances.SingleOrDefaultAsync(
                x => x.SellerId == commission.SellerId, cancellationToken)
                ?? throw new InvalidOperationException("Seller balance not found.");

            var before = checked(balance.AvailableIRR + balance.PendingIRR);
            balance.ReleasePending(commission.SellerAmountIRR);
            var after = checked(balance.AvailableIRR + balance.PendingIRR);

            db.BalanceTransactions.Add(BalanceTransaction.Create(
                ids.NewId(), commission.SellerId, orderId, null,
                BalanceTransactionType.ReleasePending,
                commission.SellerAmountIRR, before, after,
                $"DELIVERY:{orderId}"));

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