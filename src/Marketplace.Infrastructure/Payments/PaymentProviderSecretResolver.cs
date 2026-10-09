using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Marketplace.Domain.Common;

namespace Marketplace.Infrastructure.Payments;

/// <summary>Resolves "env:VARIABLE_NAME" references for sensitive JSON properties at runtime.</summary>
public sealed class PaymentProviderSecretResolver(IConfiguration configuration)
{
    public string Resolve(string configurationJson)
    {
        JsonNode root;
        try { root = JsonNode.Parse(configurationJson) ?? new JsonObject(); }
        catch (JsonException) { throw new DomainException("Payment provider configuration is invalid JSON."); }
        ResolveNode(root);
        return root.ToJsonString();
    }

    private void ResolveNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is null) continue;
                if (IsSensitive(pair.Key) && pair.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    text.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                {
                    var variableName = text[4..].Trim();
                    if (variableName.Length == 0)
                        throw new DomainException("A payment provider secret reference is empty.");
                    var secret = configuration[variableName];
                    if (string.IsNullOrWhiteSpace(secret))
                        throw new DomainException($"Required payment provider secret '{variableName}' is not configured.");
                    obj[pair.Key] = secret;
                    continue;
                }
                ResolveNode(pair.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) ResolveNode(item);
        }
    }

    private static bool IsSensitive(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("accesskey", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("merchantkey", StringComparison.Ordinal)
            || normalized.Contains("terminalkey", StringComparison.Ordinal)
            || normalized.Contains("signingkey", StringComparison.Ordinal);
    }
}
