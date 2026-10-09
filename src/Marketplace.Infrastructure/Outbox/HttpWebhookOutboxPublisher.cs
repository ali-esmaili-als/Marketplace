using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Outbox;

/// <summary>
/// Generic HTTPS webhook transport. The receiver must deduplicate Idempotency-Key
/// because delivery is at-least-once.
/// </summary>
public sealed class HttpWebhookOutboxPublisher : IOutboxPublisher
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly byte[] _secret;

    public HttpWebhookOutboxPublisher(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var endpointText = configuration["Outbox:Webhook:Url"];
        var secret = configuration["Outbox:Webhook:Secret"];
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps &&
             !(endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException("Outbox:Webhook:Url must be an absolute HTTPS URL (HTTP is allowed only for loopback development).");
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Outbox:Webhook:Secret must contain at least 32 UTF-8 bytes.");

        _endpoint = endpoint;
        _secret = Encoding.UTF8.GetBytes(secret);
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        using var payloadDocument = JsonDocument.Parse(message.PayloadJson);
        var json = JsonSerializer.Serialize(new
        {
            messageId = message.MessageId,
            eventType = message.EventType,
            occurredAtUtc = message.OccurredAtUtc,
            payload = payloadDocument.RootElement
        });
        var body = Encoding.UTF8.GetBytes(json);
        var signature = Convert.ToHexString(HMACSHA256.HashData(_secret, body)).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.MessageId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Marketplace-Signature", $"sha256={signature}");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
