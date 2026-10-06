namespace Marketplace.Application.Identity.Ports;
public interface IAuthService
{
 Task<AuthResult> LoginAsync(string mobile,string password,string? ipAddress,CancellationToken cancellationToken=default);
 Task<AuthResult> RefreshAsync(string refreshToken,string? ipAddress,CancellationToken cancellationToken=default);
 Task RevokeRefreshTokenAsync(string refreshToken,string? ipAddress,CancellationToken cancellationToken=default);
}
public sealed record AuthResult(long UserId,string AccessToken,string RefreshToken,DateTime AccessTokenExpiresAtUtc,DateTime RefreshTokenExpiresAtUtc);