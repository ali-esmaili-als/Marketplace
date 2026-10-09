using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Payments;

public sealed record PaymentProviderSettingDto(PaymentProviderCode Provider,string DisplayName,bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);

public sealed class PaymentProviderSettingsService(IPaymentProviderSettingsAdmin admin)
{
    public Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct=default)
        => admin.GetAllAsync(ct);

    public Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default)
    {
        if(sortOrder<0) throw new DomainException("Sort order cannot be negative.");

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

        return admin.ConfigureAsync(provider,isEnabled,isVisible,sortOrder,normalizedConfiguration,ct);
    }
}