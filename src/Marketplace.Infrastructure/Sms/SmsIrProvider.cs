using System.Security.Cryptography;
using System.Text.Json;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Sms;

public sealed class SmsIrProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "SmsIr";
    public string DisplayName => "SMS.ir";
    public string CreateCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public async Task SendAsync(string mobile, string message, CancellationToken ct)
    {
        var apiKey = Required("Authentication:Otp:SmsIr:ApiKey");
        var templateId = Required("Authentication:Otp:SmsIr:TemplateId");
        var parameterName = configuration["Authentication:Otp:SmsIr:ParameterName"] ?? "Code";
        var code = ExtractCode(message);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sms.ir/v1/send/verify");
        request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);
        request.Content = JsonContent.Create(new { mobile, templateId = long.Parse(templateId), parameters = new[] { new { name = parameterName, value = code } } });
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"SMS.ir rejected the OTP request. HTTP {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"SMS.ir rejected the OTP request: {body}");
    }

    private string Required(string key) => configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is required.");
    private static string ExtractCode(string message) {
        var digits = new string(message.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^Math.Min(6, digits.Length)..] : throw new InvalidOperationException("OTP code is missing.");
    }
}