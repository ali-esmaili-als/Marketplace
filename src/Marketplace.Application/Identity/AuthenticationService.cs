using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Application.Identity;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IIdentityRepository _identity;
    private readonly IPasswordHasher<Marketplace.Domain.Identity.User> _hasher;
    private readonly ITokenService _tokens;

    public AuthenticationService(IIdentityRepository identity, IPasswordHasher<Marketplace.Domain.Identity.User> hasher, ITokenService tokens)
    {
        _identity = identity;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<LoginResult> LoginAsync(string mobile, string password, CancellationToken ct = default)
    {
        var user = await _identity.GetUserByMobileAsync(mobile, ct)
            ?? throw new DomainException("Invalid credentials.");

        if (!user.IsActive)
            throw new DomainException("User is inactive.");

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            throw new DomainException("Invalid credentials.");

        user.MarkLogin();
        var rules = await _identity.GetActiveRulesForUserAsync(user.Id, ct);
        var roles = await _tokens.GetRolesAsync(user.Id, ct);
        var token = _tokens.Create(user, roles, rules.Select(x => x.Code).ToArray());

        return new LoginResult(user.Id, token.AccessToken, token.ExpiresAtUtc, roles, rules.Select(x => x.Code).ToArray());
    }
}