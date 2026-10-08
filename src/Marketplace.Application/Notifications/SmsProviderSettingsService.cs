using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Notifications;

public sealed record SmsProviderSettingDto(string Provider, string DisplayName, bool IsEnabled, bool IsVisible, int SortOrder);

public sealed class SmsProviderSettingsService(ISmsProviderSettingsAdmin admin)
{
    public Task<IReadOnlyList<SmsProviderSettingDto>> GetAllAsync(CancellationToken ct = default)
        => admin.GetAllAsync(ct);

    public Task ConfigureAsync(string provider, bool isEnabled, bool isVisible, int sortOrder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new DomainException("SMS provider is required.");
        if (sortOrder < 0) throw new DomainException("Sort order cannot be negative.");
        return admin.ConfigureAsync(provider.Trim(), isEnabled, isVisible, sortOrder, ct);
    }
}