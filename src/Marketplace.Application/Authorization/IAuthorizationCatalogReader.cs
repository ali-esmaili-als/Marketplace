namespace Marketplace.Application.Authorization;

public interface IAuthorizationCatalogReader
{
    Task<IReadOnlyList<AuthorizationActionDto>> GetActionsAsync(
        CancellationToken cancellationToken = default);
}
