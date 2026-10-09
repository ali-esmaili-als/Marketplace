using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marketplace.Application.Payments;

/// <summary>
/// Prevents payment-provider credentials from being returned to the admin UI while
/// allowing an operator to edit non-secret settings without erasing existing secrets.
/// </summary>
public static class PaymentProviderConfigurationSanitizer
{
    public const string Mask = "********";

    public static string Redact(string configurationJson)
    {
        var root = JsonNode.Parse(configurationJson);
        if (root is null) return "{}";
        RedactNode(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string MergePreservingSecrets(string existingJson, string submittedJson)
    {
        var existing = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
        var submitted = JsonNode.Parse(submittedJson) as JsonObject ?? new JsonObject();
        MergeObject(existing, submitted);
        return submitted.ToJsonString();
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (IsSensitive(pair.Key) && pair.Value is not null)
                    obj[pair.Key] = Mask;
                else if (pair.Value is not null)
                    RedactNode(pair.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) RedactNode(item);
        }
    }

    private static void MergeObject(JsonObject existing, JsonObject submitted)
    {
        foreach (var pair in existing)
        {
            if (IsSensitive(pair.Key))
            {
                if (!submitted.ContainsKey(pair.Key) ||
                    submitted[pair.Key] is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    string.Equals(text, Mask, StringComparison.Ordinal))
                {
                    submitted[pair.Key] = pair.Value?.DeepClone();
                    continue;
                }
            }

            if (pair.Value is JsonObject existingObject &&
                submitted[pair.Key] is JsonObject submittedObject)
            {
                MergeObject(existingObject, submittedObject);
            }
            else if (pair.Value is JsonArray existingArray &&
                     submitted[pair.Key] is JsonArray submittedArray)
            {
                for (var i = 0; i < Math.Min(existingArray.Count, submittedArray.Count); i++)
                {
                    if (existingArray[i] is JsonObject existingItem &&
                        submittedArray[i] is JsonObject submittedItem)
                        MergeObject(existingItem, submittedItem);
                }
            }
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