using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSellerBalanceService(
    MarketplaceDbContext db,
    IIdGenerator ids) : ISellerBalanceService
{
    public Task AddPendingAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            BalanceTransactionType.Sale,
            reference,
            (balance, amount) => balance.AddPending(amount),
            cancellationToken);

    public Task ReleasePendingAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            BalanceTransactionType.ReleasePending,
            reference,
            (balance, amount) => balance.ReleasePending(amount),
            cancellationToken);

    public Task BlockAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            BalanceTransactionType.Adjustment,
            reference,
            (balance, amount) => balance.Block(amount),
            cancellationToken);

    public Task ReleaseBlockAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            BalanceTransactionType.Adjustment,
            reference,
            (balance, amount) => balance.ReleaseBlock(amount),
            cancellationToken);

    public Task DebitAvailableAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        BalanceTransactionType transactionType,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            transactionType,
            reference,
            (balance, amount) => balance.RemoveAvailable(amount),
            cancellationToken);

    public Task AddLiabilityAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            sellerId,
            orderId,
            amountIRR,
            BalanceTransactionType.Adjustment,
            reference,
            (balance, amount) => balance.AddLiability(amount),
            cancellationToken);

    private async Task MutateAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        BalanceTransactionType transactionType,
        string reference,
        Action<SellerBalance, long> mutation,
        CancellationToken cancellationToken)
    {
        if (amountIRR <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountIRR));

        var balance = await db.SellerBalances.SingleOrDefaultAsync(
            x => x.SellerId == sellerId,
            cancellationToken)
            ?? throw new InvalidOperationException("Seller balance not found.");

        var before = balance.AvailableIRR;
        mutation(balance, amountIRR);
        var after = balance.AvailableIRR;

        db.BalanceTransactions.Add(BalanceTransaction.Create(
            ids.NewId(),
            sellerId,
            orderId,
            null,
            transactionType,
            amountIRR,
            before,
            after,
            reference));
    }
}
