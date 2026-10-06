namespace Marketplace.Application.Common.Abstractions;

public interface IResourceAccess
{
    Task EnsureAdminOrOwnerAsync(long ownerUserId, CancellationToken cancellationToken = default);
}
