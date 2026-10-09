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
            try
            {
                // EF Core does not automatically restore tracked entity values after a failed
                // transaction. Clear them after rollback so subsequent recovery/retry operations
                // reload the committed database state instead of reusing rolled-back mutations.
                await tx.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            throw;
        }
    }
}
