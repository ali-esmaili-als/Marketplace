using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSettlementService(
    MarketplaceDbContext db,
    IIdGenerator ids,
    Marketplace.Application.Common.Abstractions.ICurrentUser currentUser) : ISettlementService
{
    public async Task<long> RequestAsync(long sellerId, long bankAccountId, long amountIRR, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");

        if (sellerId != currentUser.UserId)
        {
            var isAdmin = await db.UserUserTypes.AnyAsync(
                x => x.UserId == currentUser.UserId &&
                     x.UserTypeId == Marketplace.Domain.Identity.UserTypeId.Admin,
                cancellationToken);
            if (!isAdmin)
                throw new UnauthorizedAccessException("You cannot request settlement for another seller.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await db.SellerBankAccounts.SingleOrDefaultAsync(
                x => x.Id == bankAccountId && x.SellerId == sellerId && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Active seller bank account not found.");

            var balance = await db.SellerBalances.SingleOrDefaultAsync(
                x => x.SellerId == sellerId, cancellationToken)
                ?? throw new InvalidOperationException("Seller balance not found.");

            balance.ReserveForSettlement(amountIRR);

            var settlement = Settlement.Create(
                ids.NewId(), sellerId, amountIRR, account.Id,
                account.BankName, account.Iban, account.AccountHolderName);

            db.Settlements.Add(settlement);

            var before = checked(balance.AvailableIRR + balance.PendingIRR);
            var after = checked(balance.AvailableIRR + balance.PendingIRR);

            db.BalanceTransactions.Add(BalanceTransaction.Create(
                ids.NewId(), sellerId, null, settlement.Id,
                BalanceTransactionType.Settlement, amountIRR,
                before, after, $"SETTLEMENT:{settlement.Id}"));

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