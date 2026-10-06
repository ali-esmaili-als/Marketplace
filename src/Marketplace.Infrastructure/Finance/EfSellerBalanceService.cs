using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSellerBalanceService(MarketplaceDbContext db, IIdGenerator ids) : ISellerBalanceService
{
    public Task AddPendingAsync(
        long sellerId,
        long orderId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => Change(
            sellerId,
            orderId,
            amount,
            BalanceTransactionType.Sale,
            BalanceBucket.Pending,
            reference,
            balance => balance.AddPending(amount));

    public Task ReleasePendingAsync(
        long sellerId,
        long orderId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => Transfer(
            sellerId,
            orderId,
            amount,
            BalanceTransactionType.ReleasePending,
            BalanceBucket.Pending,
            BalanceBucket.Available,
            reference,
            balance => balance.ReleasePending(amount));

    public Task BlockAsync(
        long sellerId,
        long orderId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => Transfer(
            sellerId,
            orderId,
            amount,
            BalanceTransactionType.Adjustment,
            BalanceBucket.Available,
            BalanceBucket.Blocked,
            reference,
            balance => balance.Block(amount));

    public Task ReleaseBlockAsync(
        long sellerId,
        long orderId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => Transfer(
            sellerId,
            orderId,
            amount,
            BalanceTransactionType.Adjustment,
            BalanceBucket.Blocked,
            BalanceBucket.Available,
            reference,
            balance => balance.ReleaseBlock(amount));

    public Task DebitAvailableAsync(
        long sellerId,
        long orderId,
        long amount,
        BalanceTransactionType type,
        string reference,
        CancellationToken ct = default)
        => Change(
            sellerId,
            orderId,
            amount,
            type,
            BalanceBucket.Available,
            reference,
            balance => balance.RemoveAvailable(amount));

    public Task ReserveForSettlementAsync(
        long sellerId,
        long settlementId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => ChangeSettlement(
            sellerId,
            settlementId,
            amount,
            reference,
            ct,
            BalanceTransactionType.Settlement,
            balance => balance.ReserveForSettlement(amount));

    public Task CompleteSettlementAsync(
        long sellerId,
        long settlementId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => CompleteSettlementInternal(sellerId, settlementId, amount, reference, ct);

    public Task ReleaseSettlementReservationAsync(
        long sellerId,
        long settlementId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => ChangeSettlement(
            sellerId,
            settlementId,
            amount,
            reference,
            ct,
            BalanceTransactionType.Reversal,
            balance => balance.FailSettlement(amount));

    public Task AddLiabilityAsync(
        long sellerId,
        long orderId,
        long amount,
        string reference,
        CancellationToken ct = default)
        => Change(
            sellerId,
            orderId,
            amount,
            BalanceTransactionType.Adjustment,
            BalanceBucket.Liability,
            reference,
            balance => balance.AddLiability(amount));

    private async Task CompleteSettlementInternal(
        long sellerId,
        long settlementId,
        long amount,
        string reference,
        CancellationToken ct)
    {
        ValidateAmount(amount);

        var balance = await Get(sellerId, ct);

        var beforeReserved = balance.ReservedForSettlementIRR;
        var beforeAvailable = balance.AvailableIRR;

        balance.CompleteSettlement(amount);

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                null,
                settlementId,
                BalanceTransactionType.Settlement,
                BalanceBucket.ReservedForSettlement,
                amount,
                beforeReserved,
                balance.ReservedForSettlementIRR,
                reference));

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                null,
                settlementId,
                BalanceTransactionType.Settlement,
                BalanceBucket.Available,
                amount,
                beforeAvailable,
                balance.AvailableIRR,
                reference));
    }

    private async Task ChangeSettlement(
        long sellerId,
        long settlementId,
        long amount,
        string reference,
        CancellationToken ct,
        BalanceTransactionType transactionType,
        Action<SellerBalance> action)
    {
        ValidateAmount(amount);

        var balance = await Get(sellerId, ct);
        var before = balance.ReservedForSettlementIRR;

        action(balance);

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                null,
                settlementId,
                transactionType,
                BalanceBucket.ReservedForSettlement,
                amount,
                before,
                balance.ReservedForSettlementIRR,
                reference));
    }

    private async Task Change(
        long sellerId,
        long orderId,
        long amount,
        BalanceTransactionType type,
        BalanceBucket bucket,
        string reference,
        Action<SellerBalance> action)
    {
        ValidateAmount(amount);

        var balance = await GetOrCreate(sellerId);
        var before = Value(balance, bucket);

        action(balance);

        var after = Value(balance, bucket);

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                orderId,
                null,
                type,
                bucket,
                amount,
                before,
                after,
                reference));
    }

    private async Task Transfer(
        long sellerId,
        long orderId,
        long amount,
        BalanceTransactionType type,
        BalanceBucket from,
        BalanceBucket to,
        string reference,
        Action<SellerBalance> action)
    {
        ValidateAmount(amount);

        var balance = await Get(sellerId, CancellationToken.None);
        var beforeFrom = Value(balance, from);
        var beforeTo = Value(balance, to);

        action(balance);

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                orderId,
                null,
                type,
                from,
                amount,
                beforeFrom,
                Value(balance, from),
                reference));

        db.BalanceTransactions.Add(
            BalanceTransaction.Create(
                ids.NewId(),
                sellerId,
                orderId,
                null,
                type,
                to,
                amount,
                beforeTo,
                Value(balance, to),
                reference));
    }

    private async Task<SellerBalance> Get(long sellerId, CancellationToken ct)
        => await db.SellerBalances.SingleOrDefaultAsync(x => x.SellerId == sellerId, ct)
           ?? throw new InvalidOperationException("Seller balance not found.");

    private async Task<SellerBalance> GetOrCreate(long sellerId)
    {
        var balance = await db.SellerBalances.SingleOrDefaultAsync(x => x.SellerId == sellerId);
        if (balance is not null)
            return balance;

        balance = SellerBalance.Create(ids.NewId(), sellerId);
        db.SellerBalances.Add(balance);
        return balance;
    }

    private static long Value(SellerBalance balance, BalanceBucket bucket) => bucket switch
    {
        BalanceBucket.Available => balance.AvailableIRR,
        BalanceBucket.Pending => balance.PendingIRR,
        BalanceBucket.Blocked => balance.BlockedIRR,
        BalanceBucket.ReservedForSettlement => balance.ReservedForSettlementIRR,
        BalanceBucket.Liability => balance.LiabilityIRR,
        _ => throw new InvalidOperationException("Unsupported balance bucket.")
    };

    private static void ValidateAmount(long amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
    }
}
