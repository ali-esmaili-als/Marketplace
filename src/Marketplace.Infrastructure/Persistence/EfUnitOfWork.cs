using System.Data;
using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Persistence;

public sealed class EfUnitOfWork(MarketplaceDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct=default)=>db.SaveChangesAsync(ct);

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken ct=default)
        => ExecuteAsync(action,IsolationLevel.ReadCommitted,ct);

    public Task<T> ExecuteInSerializableTransactionAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken ct=default)
        => ExecuteAsync(action,IsolationLevel.Serializable,ct);

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> action,IsolationLevel isolation,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(isolation,ct);
        try
        {
            var result=await action(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
