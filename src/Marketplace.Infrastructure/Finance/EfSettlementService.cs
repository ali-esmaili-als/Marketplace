using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSettlementService(
    MarketplaceDbContext db,
    IIdGenerator ids,
    Marketplace.Application.Common.Abstractions.IResourceAccess resourceAccess, ISellerBalanceService sellerBalance) : ISettlementService
{
    public async Task<long> RequestAsync(long sellerId, long bankAccountId, long amountIRR, CancellationToken cancellationToken = default)
    {
        await resourceAccess.EnsureAdminOrOwnerAsync(sellerId, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await db.SellerBankAccounts.SingleOrDefaultAsync(
                x => x.Id == bankAccountId && x.SellerId == sellerId && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Active seller bank account not found.");

            // Reservation is handled by the balance service.

            var settlement = Settlement.Create(
                ids.NewId(), sellerId, amountIRR, account.Id,
                account.BankName, account.Iban, account.AccountHolderName);

            db.Settlements.Add(settlement);

            await sellerBalance.ReserveForSettlementAsync(sellerId, settlement.Id, amountIRR, $"SETTLEMENT:{settlement.Id}", cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return settlement.Id;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}