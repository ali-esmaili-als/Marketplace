using System.Data;
using Microsoft.Data.SqlClient;
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
        const int maxDeadlockRetries = 3;

        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(isolation, ct);
            try
            {
                var result = await action(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    // EF Core does not automatically restore tracked entity values after a failed
                    // transaction. Clear them after rollback so retries reload committed state.
                    await tx.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the original transaction/action failure.
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }

                // SQL Server deadlock victims are safe to retry only by rerunning the complete
                // database transaction. Never retry just SaveChanges: the reads and domain
                // decisions must be reevaluated against the new committed state.
                if (attempt >= maxDeadlockRetries || !IsSqlServerDeadlock(ex))
                    throw;

                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
            }
        }
    }

    private static bool IsSqlServerDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
                return true;
        }

        return false;
    }
}
