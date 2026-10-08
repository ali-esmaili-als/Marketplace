using Marketplace.Application.Notifications;

namespace Marketplace.Application.Abstractions;

public interface ISmsProviderSettingsAdmin
{
    Task<IReadOnlyList<SmsProviderSettingDto>> GetAllAsync(CancellationToken ct = default);
    Task ConfigureAsync(string provider, bool isEnabled, bool isVisible, int sortOrder, CancellationToken ct = default);
}