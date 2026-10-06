namespace Marketplace.Infrastructure.Identity;
public sealed class JwtOptions
{
 public string Issuer{get;init;}="Marketplace"; public string Audience{get;init;}="Marketplace.Api"; public string SigningKey{get;init;}="";
 public int AccessTokenMinutes{get;init;}=15; public int RefreshTokenDays{get;init;}=30;
}