using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Sms;

public sealed class SmsIrProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "SmsIr";
    public string DisplayName => "SMS.ir";
    public string CreateCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public async Task SendOtpAsync(string mobile, string code, CancellationToken ct)
    {
        var apiKey = Required("Authentication:Otp:SmsIr:ApiKey");
        var templateIdText = Required("Authentication:Otp:SmsIr:TemplateId");
        if (!long.TryParse(templateIdText, out var templateId) || templateId <= 0)
            throw new InvalidOperationException("Authentication:Otp:SmsIr:TemplateId must be a positive integer.");

        var parameterName = configuration["Authentication:Otp:SmsIr:ParameterName"] ?? "Code";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sms.ir/v1/send/verify");
        request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new
        {
            mobile,
            templateId,
            parameters = new[] { new { name = parameterName, value = code } }
        });

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"SMS.ir rejected the OTP request. HTTP {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"SMS.ir rejected the OTP request: {body}");
    }

    private string Required(string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required.");
}