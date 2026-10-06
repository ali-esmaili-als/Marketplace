namespace Marketplace.Application.Abstractions;

public sealed record LoginResult(long UserId, string AccessToken, DateTime ExpiresAtUtc, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(string mobile, string password, CancellationToken ct = default);
}