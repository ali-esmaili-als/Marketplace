using Marketplace.Application.Finance.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfFinanceService(MarketplaceDbContext db, IIdGenerator ids, IClock clock) : IFinanceService
{
    public async Task ReleaseSellerFundsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        var commission = await db.Commissions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Commission not found.");

        var balance = await db.SellerBalances.SingleOrDefaultAsync(x => x.SellerId == commission.SellerId, cancellationToken)
            ?? throw new InvalidOperationException("Seller balance not found.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = balance.AvailableIRR + balance.PendingIRR;
            balance.ReleasePending(commission.SellerAmountIRR);
            var after = balance.AvailableIRR + balance.PendingIRR;

            db.BalanceTransactions.Add(BalanceTransaction.Create(
                ids.NewId(), commission.SellerId, orderId, null,
                BalanceTransactionType.ReleasePending, commission.SellerAmountIRR,
                before, after, $"DELIVERY:{orderId}"));

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