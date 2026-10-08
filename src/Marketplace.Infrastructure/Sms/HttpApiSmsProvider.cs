using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Sms;

public sealed class HttpApiSmsProvider(HttpClient http, IConfiguration configuration) : ISmsProvider
{
    public string Name => "HttpApi";
    public string DisplayName => "سرویس پیامکی HTTP API";
    public string CreateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public async Task SendOtpAsync(string mobile, string code, CancellationToken ct)
    {
        var endpoint = configuration["Authentication:Otp:HttpApi:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "A valid HTTPS Authentication:Otp:HttpApi:Endpoint is required for the HttpApi SMS provider.");

        var message = $"Your Marketplace verification code is {code}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { mobile, code, message }),
                Encoding.UTF8,
                "application/json")
        };

        var apiKey = configuration["Authentication:Otp:HttpApi:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"HTTP API SMS provider rejected the OTP request. HTTP {(int)response.StatusCode}: {body}");
    }
}