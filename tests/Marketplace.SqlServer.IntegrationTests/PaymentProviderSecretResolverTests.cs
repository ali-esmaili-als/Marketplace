using Microsoft.Extensions.Configuration;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Payments;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

public sealed class PaymentProviderSecretResolverTests
{
    [Fact]
    public void Resolve_ReplacesEnvironmentReferenceForSensitiveProperty()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PAYMENT_MELLI_PASSWORD"] = "test-secret-value" })
            .Build();
        var resolver = new PaymentProviderSecretResolver(configuration);
        var result = resolver.Resolve("""{"Password":"env:PAYMENT_MELLI_PASSWORD","MerchantId":"public-merchant"}""");
        Assert.Contains("\"Password\":\"test-secret-value\"", result);
        Assert.Contains("\"MerchantId\":\"public-merchant\"", result);
    }

    [Fact]
    public void Resolve_DoesNotResolveEnvironmentReferenceForNonSecretProperty()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PAYMENT_CALLBACK"] = "https://example.test/callback" })
            .Build();
        var resolver = new PaymentProviderSecretResolver(configuration);
        var result = resolver.Resolve("""{"CallbackUrl":"env:PAYMENT_CALLBACK"}""");
        Assert.Contains("env:PAYMENT_CALLBACK", result);
    }

    [Fact]
    public void Resolve_MissingSecretThrowsWithoutReturningConfiguration()
    {
        var resolver = new PaymentProviderSecretResolver(new ConfigurationBuilder().Build());
        var exception = Assert.Throws<DomainException>(() => resolver.Resolve("""{"Password":"env:PAYMENT_MELLI_PASSWORD"}"""));
        Assert.Contains("PAYMENT_MELLI_PASSWORD", exception.Message);
        Assert.DoesNotContain("test-secret-value", exception.Message);
    }

    [Fact]
    public void Resolve_ResolvesNestedSecretProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PAYMENT_SECRET"] = "nested-test-secret" })
            .Build();
        var resolver = new PaymentProviderSecretResolver(configuration);
        var result = resolver.Resolve("""{"credentials":{"apiKey":"env:PAYMENT_SECRET"}}""");
        Assert.Contains("nested-test-secret", result);
    }
}
