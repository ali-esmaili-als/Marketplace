using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Finance;

public sealed class EfSellerBalanceService(MarketplaceDbContext db, IIdGenerator ids) : ISellerBalanceService
{
    public Task AddPendingAsync(long sellerId,long orderId,long amount,string reference,CancellationToken ct=default)
        => Change(sellerId,orderId,amount,BalanceTransactionType.Sale,BalanceBucket.Pending,reference,
            b=>b.AddPending(amount));

    public Task ReleasePendingAsync(long sellerId,long orderId,long amount,string reference,CancellationToken ct=default)
        => Transfer(sellerId,orderId,amount,BalanceTransactionType.ReleasePending,BalanceBucket.Pending,BalanceBucket.Available,reference,b=>b.ReleasePending(amount));

    public Task BlockAsync(long sellerId,long orderId,long amount,string reference,CancellationToken ct=default)
        => Transfer(sellerId,orderId,amount,BalanceTransactionType.Adjustment,BalanceBucket.Available,BalanceBucket.Blocked,reference,b=>b.Block(amount));

    public Task ReleaseBlockAsync(long sellerId,long orderId,long amount,string reference,CancellationToken ct=default)
        => Transfer(sellerId,orderId,amount,BalanceTransactionType.Adjustment,BalanceBucket.Blocked,BalanceBucket.Available,reference,b=>b.ReleaseBlock(amount));

    public Task DebitAvailableAsync(long sellerId,long orderId,long amount,BalanceTransactionType type,string reference,CancellationToken ct=default)
        => Change(sellerId,orderId,amount,type,BalanceBucket.Available,reference,b=>b.RemoveAvailable(amount));

    public Task AddLiabilityAsync(long sellerId,long orderId,long amount,string reference,CancellationToken ct=default)
        => Change(sellerId,orderId,amount,BalanceTransactionType.Adjustment,BalanceBucket.Liability,reference,b=>b.AddLiability(amount));

    private async Task Change(long sellerId,long orderId,long amount,BalanceTransactionType type,BalanceBucket bucket,string reference,Action<SellerBalance> action)
    {
        if(amount<=0) throw new ArgumentOutOfRangeException(nameof(amount));
        var b=await Get(sellerId);
        var before=Value(b,bucket);
        action(b);
        var after=bucket switch
        {
            BalanceBucket.Available=>b.AvailableIRR,
            BalanceBucket.Pending=>b.PendingIRR,
            BalanceBucket.Liability=>b.LiabilityIRR,
            _=>throw new InvalidOperationException()
        };
        db.BalanceTransactions.Add(BalanceTransaction.Create(ids.NewId(),sellerId,orderId,null,type,bucket,amount,before,after,reference));
    }

    private async Task Transfer(long sellerId,long orderId,long amount,BalanceTransactionType type,BalanceBucket from,BalanceBucket to,string reference,Action<SellerBalance> action)
    {
        if(amount<=0) throw new ArgumentOutOfRangeException(nameof(amount));
        var b=await Get(sellerId);
        var beforeFrom=Value(b,from); var beforeTo=Value(b,to);
        action(b);
        db.BalanceTransactions.Add(BalanceTransaction.Create(ids.NewId(),sellerId,orderId,null,type,from,amount,beforeFrom,Value(b,from),reference));
        db.BalanceTransactions.Add(BalanceTransaction.Create(ids.NewId(),sellerId,orderId,null,type,to,amount,beforeTo,Value(b,to),reference));
    }

    private async Task<SellerBalance> Get(long sellerId)
        => await db.SellerBalances.SingleOrDefaultAsync(x=>x.SellerId==sellerId)
           ?? throw new InvalidOperationException("Seller balance not found.");

    private static long Value(SellerBalance b,BalanceBucket x)=>x switch
    {
        BalanceBucket.Available=>b.AvailableIRR,
        BalanceBucket.Pending=>b.PendingIRR,
        BalanceBucket.Blocked=>b.BlockedIRR,
        BalanceBucket.Liability=>b.LiabilityIRR,
        _=>throw new InvalidOperationException()
    };
}