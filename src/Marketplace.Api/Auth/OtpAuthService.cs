using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.Extensions.Caching.Memory;

namespace Marketplace.Api.Auth;

public sealed record OtpRequestResult(int ExpiresInSeconds, int RetryAfterSeconds, string? MaskedMobile);
public sealed record OtpLoginResult(long UserId, string Mobile, string DisplayName, IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions, string AccessToken, DateTime ExpiresAtUtc, bool IsNewUser = false);

public sealed class OtpAuthService(
    IConfiguration configuration,
    IEnumerable<ISmsProvider> providers,
    ISmsProviderSettings settings,
    IMemoryCache cache,
    IIdentityRepository identity,
    ITokenService tokens,
    IUnitOfWork unitOfWork)
{
    private static string CacheKey(string mobile) => $"auth:otp:{mobile}";

    public async Task<(bool OtpEnabled, string? ProviderName)> GetOptionsAsync(CancellationToken ct = default)
    {
        var selected = await settings.GetSelectedAsync(ct);
        if (selected is null) return (false, null);

        var provider = providers.FirstOrDefault(x => string.Equals(x.Name, selected.Provider, StringComparison.OrdinalIgnoreCase));
        return provider is null ? (false, null) : (true, provider.DisplayName);
    }

    private async Task<ISmsProvider> GetSelectedProviderAsync(CancellationToken ct)
    {
        var selected = await settings.GetSelectedAsync(ct);
        if (selected is null)
            throw new DomainException("OTP login is disabled because no SMS provider is selected.");

        return providers.FirstOrDefault(x => string.Equals(x.Name, selected.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"SMS provider '{selected.Provider}' is enabled in database but is not registered.");
    }

    public async Task<OtpRequestResult> RequestAsync(string mobile, CancellationToken ct)
    {
        var provider = await GetSelectedProviderAsync(ct);
        mobile = NormalizeMobile(mobile);
        var throttleKey = $"auth:otp:throttle:{mobile}";
        if (cache.TryGetValue(throttleKey, out _))
            throw new DomainException("Please wait before requesting another verification code.");

        var code = provider.CreateCode();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        // Store a hash rather than the raw code. Test provider deliberately uses the fixed code 1234.
        cache.Set(CacheKey(mobile), Hash(code), new MemoryCacheEntryOptions { AbsoluteExpiration = expiresAt });
        cache.Set(throttleKey, true, TimeSpan.FromSeconds(30));
        try
        {
            if (!string.Equals(provider.Name, "Test", StringComparison.OrdinalIgnoreCase))
                await provider.SendOtpAsync(mobile, code, ct);
        }
        catch
        {
            cache.Remove(CacheKey(mobile));
            cache.Remove(throttleKey);
            throw;
        }

        var masked = mobile.Length > 4 ? new string('*', mobile.Length - 4) + mobile[^4..] : mobile;
        return new OtpRequestResult(300, 30, masked);
    }

    public async Task<OtpLoginResult> VerifyAsync(string mobile, string otp, CancellationToken ct)
    {
        _ = await GetSelectedProviderAsync(ct);
        mobile = NormalizeMobile(mobile);
        if (!cache.TryGetValue<string>(CacheKey(mobile), out var expected))
            throw new DomainException("Invalid or expired verification code.");

        var suppliedHash = Hash((otp ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(otp) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected!), Encoding.UTF8.GetBytes(suppliedHash)))
        {
            var attemptsKey = $"auth:otp:attempts:{mobile}";
            var attempts = cache.Get<int>(attemptsKey) + 1;
            cache.Set(attemptsKey, attempts, TimeSpan.FromMinutes(5));
            if (attempts >= 5) cache.Remove(CacheKey(mobile));
            throw new DomainException(attempts >= 5
                ? "Too many invalid verification attempts. Request a new code."
                : "Invalid or expired verification code.");
        }

        cache.Remove(CacheKey(mobile));
        cache.Remove($"auth:otp:attempts:{mobile}");
        var user = await identity.GetUserByMobileAsync(mobile, ct)
            ?? throw new DomainException("No account exists for this mobile number. Register or use password login first.");
        if (!user.IsActive) throw new DomainException("User is inactive.");

        user.SetMobileVerified();
        user.MarkLogin();
        await unitOfWork.SaveChangesAsync(ct);
        var rules = await identity.GetActiveRulesForUserAsync(user.Id, ct);
        var roles = await tokens.GetRolesAsync(user.Id, ct);
        var token = tokens.Create(user, roles, rules.Select(x => x.Code).ToArray());
        return new OtpLoginResult(user.Id, user.Mobile, user.DisplayName, roles, rules.Select(x => x.Code).ToArray(),
            token.AccessToken, token.ExpiresAtUtc);
    }

    private static string NormalizeMobile(string mobile)
    {
        var value = (mobile ?? string.Empty).Trim().Replace(" ", "").Replace("-", "");
        if (value.StartsWith("+98", StringComparison.Ordinal)) value = "0" + value[3..];
        else if (value.StartsWith("0098", StringComparison.Ordinal)) value = "0" + value[4..];
        if (value.Length is < 10 or > 15) throw new DomainException("Invalid mobile number.");
        return value;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}