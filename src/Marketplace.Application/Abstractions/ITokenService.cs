using Marketplace.Domain.Identity;

namespace Marketplace.Application.Abstractions;

public sealed record AccessTokenResult(string AccessToken, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessTokenResult Create(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
    Task<IReadOnlyCollection<string>> GetRolesAsync(long userId, CancellationToken ct = default);
}