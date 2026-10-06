using Marketplace.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Marketplace.Infrastructure.Persistence;

public sealed class InfrastructureUnitOfWork(MarketplaceDbContext db) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            _transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    }
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}