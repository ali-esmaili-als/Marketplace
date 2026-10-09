using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

public sealed class HttpWebhookOutboxPublisherTests
{
    [Fact]
    public async Task Publish_sends_idempotency_key_and_hmac_of_exact_body()
    {
        HttpRequestMessage? captured = null;
        byte[]? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsByteArrayAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = new HttpClient(handler);
        const string secret = "0123456789abcdef0123456789abcdef";
        var publisher = new HttpWebhookOutboxPublisher(client, Configuration(secret));
        var id = Guid.Parse("9f7c7f19-9cb0-4b6d-8d37-7e2c7d20c1c4");

        await publisher.PublishAsync(new OutboxEnvelope(id, "Settlement.Completed", "{\"settlementId\":42}", DateTime.UtcNow));

        Assert.NotNull(captured);
        Assert.Equal(id.ToString("D"), captured!.Headers.GetValues("Idempotency-Key").Single());
        Assert.NotNull(capturedBody);
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), capturedBody!)).ToLowerInvariant();
        Assert.Equal($"sha256={expected}", captured.Headers.GetValues("X-Marketplace-Signature").Single());
        using var json = JsonDocument.Parse(capturedBody!);
        Assert.Equal(id, json.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal("Settlement.Completed", json.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("payload").GetProperty("settlementId").GetInt32());
    }

    [Fact]
    public async Task Non_success_webhook_response_is_retried_by_dispatcher()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var publisher = new HttpWebhookOutboxPublisher(client, Configuration("0123456789abcdef0123456789abcdef"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            publisher.PublishAsync(new OutboxEnvelope(Guid.NewGuid(), "Settlement.Requested", "{}", DateTime.UtcNow)));
    }

    [Fact]
    public void Rejects_non_https_remote_endpoint_and_short_secret()
    {
        var httpConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbox:Webhook:Url"] = "http://example.com/events",
            ["Outbox:Webhook:Secret"] = "0123456789abcdef0123456789abcdef"
        }).Build();
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        Assert.Throws<InvalidOperationException>(() => new HttpWebhookOutboxPublisher(client, httpConfig));

        var shortSecretConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbox:Webhook:Url"] = "https://example.com/events",
            ["Outbox:Webhook:Secret"] = "short"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new HttpWebhookOutboxPublisher(client, shortSecretConfig));
    }

    private static IConfiguration Configuration(string secret) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbox:Webhook:Url"] = "http://localhost/events",
            ["Outbox:Webhook:Secret"] = secret
        }).Build();

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => response(request);
    }
}
