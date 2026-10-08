using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Sms;

public sealed class MelipayamakSmsProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "Melipayamak";
    public string DisplayName => "ملی پیامک";
    public string CreateCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public async Task SendOtpAsync(string mobile, string code, CancellationToken ct)
    {
        var username = Required("Authentication:Otp:Melipayamak:Username");
        var password = Required("Authentication:Otp:Melipayamak:Password");
        var from = configuration["Authentication:Otp:Melipayamak:From"];
        var bodyId = configuration["Authentication:Otp:Melipayamak:BodyId"];

        using HttpResponseMessage response;
        if (int.TryParse(bodyId, out var templateId) && templateId > 0)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["text"] = code,
                ["to"] = mobile,
                ["bodyId"] = templateId.ToString()
            });
            response = await http.PostAsync(
                "https://rest.payamak-panel.com/api/SendSMS/BaseServiceNumber",
                content,
                ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(from))
                throw new InvalidOperationException(
                    "Authentication:Otp:Melipayamak:From or BodyId is required.");

            var message = $"Your Marketplace verification code is {code}";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["to"] = mobile,
                ["from"] = from,
                ["text"] = message,
                ["isFlash"] = "false"
            });
            response = await http.PostAsync(
                "https://rest.payamak-panel.com/api/SendSMS/SendSMS",
                content,
                ct);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Melipayamak rejected the OTP request. HTTP {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("RetStatus", out var status) &&
            status.GetInt32() != 1)
            throw new InvalidOperationException($"Melipayamak rejected the OTP request: {body}");
    }

    private string Required(string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required.");
}