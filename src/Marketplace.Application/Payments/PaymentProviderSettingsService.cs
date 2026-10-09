using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Payments;

public sealed record PaymentProviderSettingDto(
    PaymentProviderCode Provider,
    string DisplayName,
    bool IsEnabled,
    bool IsVisible,
    int SortOrder,
    string ConfigurationJson,
    bool IsProtocolImplemented,
    string ReadinessMessage);

public sealed class PaymentProviderSettingsService(IPaymentProviderSettingsAdmin admin)
{
    public Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct=default)
        => admin.GetAllAsync(ct);

    public Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default,long? adminUserId=null)
    {
        if(sortOrder<0) throw new DomainException("Sort order cannot be negative.");

        if (!PaymentProviderCapabilities.IsProtocolImplemented(provider) && (isEnabled || isVisible))
            throw new DomainException("This payment provider has no implemented bank protocol and cannot be enabled or shown to customers.");

        var normalizedConfiguration = string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson;
        try
        {
            using var document = JsonDocument.Parse(normalizedConfiguration);
            if(document.RootElement.ValueKind != JsonValueKind.Object)
                throw new DomainException("Payment provider configuration must be a JSON object.");
        }
        catch(JsonException)
        {
            throw new DomainException("Payment provider configuration must be valid JSON.");
        }

        return admin.ConfigureAsync(provider,isEnabled,isVisible,sortOrder,normalizedConfiguration,ct,adminUserId);
    }
}