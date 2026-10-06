using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSettlementService(
    MarketplaceDbContext db,
    IIdGenerator ids,
    IResourceAccess resourceAccess,
    ISellerBalanceService sellerBalance) : ISettlementService
{
    public async Task<long> RequestAsync(
        long sellerId,
        long bankAccountId,
        long amountIRR,
        CancellationToken cancellationToken = default)
    {
        await resourceAccess.EnsureAdminOrOwnerAsync(sellerId, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var account = await db.SellerBankAccounts.SingleOrDefaultAsync(
                x => x.Id == bankAccountId &&
                     x.SellerId == sellerId &&
                     x.IsActive,
                cancellationToken)
                ?? throw new InvalidOperationException("Active seller bank account not found.");

            var settlement = Settlement.Create(
                ids.NewId(),
                sellerId,
                amountIRR,
                account.Id,
                account.BankName,
                account.Iban,
                account.AccountHolderName);

            db.Settlements.Add(settlement);

            await sellerBalance.ReserveForSettlementAsync(
                sellerId,
                settlement.Id,
                amountIRR,
                $"SETTLEMENT:RESERVE:{settlement.Id}",
                cancellationToken);

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

    public async Task MarkProcessingAsync(
        long settlementId,
        CancellationToken cancellationToken = default)
    {
        var settlement = await GetSettlement(settlementId, cancellationToken);
        settlement.MarkProcessing();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        long settlementId,
        string gatewayReference,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var settlement = await GetSettlement(settlementId, cancellationToken);

            settlement.Complete(gatewayReference);

            await sellerBalance.CompleteSettlementAsync(
                settlement.SellerId,
                settlement.Id,
                settlement.AmountIRR,
                $"SETTLEMENT:COMPLETE:{settlement.Id}",
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

    public async Task FailAsync(
        long settlementId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var settlement = await GetSettlement(settlementId, cancellationToken);
            settlement.Fail(reason);

            await sellerBalance.ReleaseSettlementReservationAsync(
                settlement.SellerId,
                settlement.Id,
                settlement.AmountIRR,
                $"SETTLEMENT:RELEASE:{settlement.Id}",
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

    public async Task CancelAsync(
        long settlementId,
        CancellationToken cancellationToken = default)
    {
        var settlement = await GetSettlement(settlementId, cancellationToken);

        await resourceAccess.EnsureAdminOrOwnerAsync(
            settlement.SellerId,
            cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            settlement.Cancel();

            await sellerBalance.ReleaseSettlementReservationAsync(
                settlement.SellerId,
                settlement.Id,
                settlement.AmountIRR,
                $"SETTLEMENT:CANCEL:{settlement.Id}",
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

    private async Task<Settlement> GetSettlement(
        long settlementId,
        CancellationToken cancellationToken)
        => await db.Settlements.SingleOrDefaultAsync(
            x => x.Id == settlementId,
            cancellationToken)
           ?? throw new InvalidOperationException("Settlement not found.");
}
