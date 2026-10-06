namespace Marketplace.Application.Abstractions;

public interface IIdGenerator
{
    Task<long> NextAsync(CancellationToken cancellationToken=default);
}