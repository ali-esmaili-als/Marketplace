using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Sms;

public sealed class KavenegarSmsProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "Kavenegar";
    public string DisplayName => "کاوه نگار";
    public string CreateCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public async Task SendOtpAsync(string mobile, string code, CancellationToken ct)
    {
        var apiKey = Required("Authentication:Otp:Kavenegar:ApiKey");
        var template = Required("Authentication:Otp:Kavenegar:Template");
        var type = configuration["Authentication:Otp:Kavenegar:Type"] ?? "sms";
        var url = $"https://api.kavenegar.com/v1/{Uri.EscapeDataString(apiKey)}/verify/lookup.json?receptor={Uri.EscapeDataString(mobile)}&token={Uri.EscapeDataString(code)}&template={Uri.EscapeDataString(template)}&type={Uri.EscapeDataString(type)}";

        using var response = await http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Kavenegar rejected the OTP request. HTTP {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("return", out var result) &&
            result.TryGetProperty("status", out var status) &&
            status.GetInt32() != 200)
            throw new InvalidOperationException($"Kavenegar rejected the OTP request: {body}");
    }

    private string Required(string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required.");
    public async Task SendMessageAsync(string mobile, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 1000) throw new ArgumentException("SMS message must contain 1-1000 characters.", nameof(message));
        var apiKey = Required("Authentication:Otp:Kavenegar:ApiKey");
        var sender = configuration["Notifications:Sms:Kavenegar:Sender"] ?? configuration["Authentication:Otp:Kavenegar:Sender"];
        if (string.IsNullOrWhiteSpace(sender)) throw new InvalidOperationException("Notifications:Sms:Kavenegar:Sender is required for automatic SMS.");
        var url = $"https://api.kavenegar.com/v1/{Uri.EscapeDataString(apiKey)}/sms/send.json?receptor={Uri.EscapeDataString(mobile)}&sender={Uri.EscapeDataString(sender)}&message={Uri.EscapeDataString(message)}";
        using var response = await http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Kavenegar rejected the SMS. HTTP {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("return", out var result) && result.TryGetProperty("status", out var status) && status.GetInt32() != 200)
            throw new InvalidOperationException($"Kavenegar rejected the SMS: {body}");
    }
}