using Marketplace.Application.Finance.Ports;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfFinanceService(MarketplaceDbContext db, ISellerBalanceService sellerBalance) : IFinanceService
{
    public async Task ReleaseSellerFundsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var commission = await db.Commissions.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Commission not found.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await sellerBalance.ReleasePendingAsync(commission.SellerId, orderId, commission.SellerAmountIRR, $"DELIVERY:{orderId}", cancellationToken);
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
