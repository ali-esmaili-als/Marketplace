using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.Extensions.Caching.Memory;

namespace Marketplace.Api.Auth;

public interface ISmsProvider
{
    string Name { get; }
    Task SendAsync(string mobile, string message, CancellationToken ct);
    string CreateCode();
}

public sealed class TestSmsProvider : ISmsProvider
{
    public string Name => "Test";
    public string CreateCode() => "1234";
    public Task SendAsync(string mobile, string message, CancellationToken ct) => Task.CompletedTask;
}

public sealed class HttpApiSmsProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "HttpApi";
    public string CreateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public async Task SendAsync(string mobile, string message, CancellationToken ct)
    {
        var endpoint = configuration["Authentication:Otp:HttpApi:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A valid HTTPS Authentication:Otp:HttpApi:Endpoint is required for the HttpApi SMS provider.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { mobile, message }), Encoding.UTF8, "application/json")
        };
        var apiKey = configuration["Authentication:Otp:HttpApi:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

public sealed record OtpRequestResult(int ExpiresInSeconds, int RetryAfterSeconds, string? MaskedMobile);
public sealed record OtpLoginResult(long UserId, string Mobile, string DisplayName, IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions, string AccessToken, DateTime ExpiresAtUtc, bool IsNewUser = false);

public sealed class OtpAuthService(
    IConfiguration configuration,
    IEnumerable<ISmsProvider> providers,
    IMemoryCache cache,
    IIdentityRepository identity,
    ITokenService tokens,
    IUnitOfWork unitOfWork)
{
    private static string CacheKey(string mobile) => $"auth:otp:{mobile}";
    private string? SelectedProvider => configuration["Authentication:Otp:Provider"]?.Trim();
    public bool IsEnabled => !string.IsNullOrWhiteSpace(SelectedProvider) && providers.Any(x => string.Equals(x.Name, SelectedProvider, StringComparison.OrdinalIgnoreCase));

    public async Task<OtpRequestResult> RequestAsync(string mobile, CancellationToken ct)
    {
        var provider = GetSelectedProvider();
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
                await provider.SendAsync(mobile, $"Your Marketplace verification code is {code}", ct);
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
        _ = GetSelectedProvider();
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

    private ISmsProvider GetSelectedProvider()
    {
        if (string.IsNullOrWhiteSpace(SelectedProvider))
            throw new DomainException("OTP login is disabled because no SMS provider is configured.");
        return providers.FirstOrDefault(x => string.Equals(x.Name, SelectedProvider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"SMS provider '{SelectedProvider}' is not registered. Configure a supported provider or add its implementation.");
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