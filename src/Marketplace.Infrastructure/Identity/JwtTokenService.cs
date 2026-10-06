using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Identity;

namespace Marketplace.Infrastructure.Identity;

public sealed class JwtTokenService(IConfiguration configuration, MarketplaceDbContext db) : ITokenService
{
    public AccessTokenResult Create(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var key = configuration["Authentication:Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("Authentication:Jwt:Key must be configured with at least 32 characters.");

        var issuer = configuration["Authentication:Jwt:Issuer"] ?? "Marketplace";
        var audience = configuration["Authentication:Jwt:Audience"] ?? "Marketplace.Client";
        var minutes = int.TryParse(configuration["Authentication:Jwt:AccessTokenMinutes"], out var m) ? m : 60;
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName)
        };
        claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
        claims.AddRange(permissions.Select(x => new Claim("permission", x)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: credentials);
        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(long userId, CancellationToken ct = default)
        => await db.UserRoles.Where(x => x.UserId == userId).Join(db.Roles, x => x.RoleId, x => x.Id, (_, role) => role.Name).ToListAsync(ct);
}