using System.Text.Json.Nodes;
using Marketplace.Application.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentProviderConfigurationSanitizerTests
{
    [Fact]
    public void Redact_masks_secrets_recursively_but_keeps_non_secret_settings()
    {
        var result = JsonNode.Parse(PaymentProviderConfigurationSanitizer.Redact(
            """{"MerchantId":"merchant-1","ApiKey":"live-secret","nested":{"client_secret":"secret-2","timeoutSeconds":30}}"""))!;

        Assert.Equal("merchant-1", (string?)result["MerchantId"]);
        Assert.Equal(PaymentProviderConfigurationSanitizer.Mask, (string?)result["ApiKey"]);
        Assert.Equal(PaymentProviderConfigurationSanitizer.Mask, (string?)result["nested"]?["client_secret"]);
        Assert.Equal(30, (int?)result["nested"]?["timeoutSeconds"]);
    }

    [Fact]
    public void Merge_preserves_masked_secret_when_non_secret_setting_changes()
    {
        var merged = JsonNode.Parse(PaymentProviderConfigurationSanitizer.MergePreservingSecrets(
            """{"ApiKey":"live-secret","merchantId":"m-1","options":{"clientSecret":"nested-secret","timeout":10}}""",
            """{"ApiKey":"********","merchantId":"m-2","options":{"clientSecret":"********","timeout":20}}"""))!;

        Assert.Equal("live-secret", (string?)merged["ApiKey"]);
        Assert.Equal("m-2", (string?)merged["merchantId"]);
        Assert.Equal("nested-secret", (string?)merged["options"]?["clientSecret"]);
        Assert.Equal(20, (int?)merged["options"]?["timeout"]);
    }

    [Fact]
    public void Merge_preserves_omitted_secret_but_accepts_explicit_replacement()
    {
        var merged = JsonNode.Parse(PaymentProviderConfigurationSanitizer.MergePreservingSecrets(
            """{"apiKey":"old-secret","timeout":10}""",
            """{"apiKey":"new-secret","timeout":20}"""))!;

        Assert.Equal("new-secret", (string?)merged["apiKey"]);
        Assert.Equal(20, (int?)merged["timeout"]);
    }

    [Fact]
    public void Redact_does_not_expose_secrets_inside_arrays()
    {
        var result = JsonNode.Parse(PaymentProviderConfigurationSanitizer.Redact(
            """{"endpoints":[{"access_token":"secret","enabled":true}]}"""))!;

        Assert.Equal(PaymentProviderConfigurationSanitizer.Mask, (string?)result["endpoints"]?[0]?["access_token"]);
        Assert.True((bool)result["endpoints"]![0]!["enabled"]!);
    }
}