using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.Persistence;

public sealed class EfUnitOfWork(MarketplaceDbContext db):IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct=default)=>db.SaveChangesAsync(ct);
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken ct=default)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        try{var result=await action(ct);await tx.CommitAsync(ct);return result;}
        catch{await tx.RollbackAsync(ct);throw;}
    }
}