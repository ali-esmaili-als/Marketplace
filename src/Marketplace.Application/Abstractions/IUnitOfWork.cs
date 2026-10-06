namespace Marketplace.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken=default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken cancellationToken=default);
}