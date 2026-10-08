using Marketplace.Domain.Common;

namespace Marketplace.Domain.Notifications;

public sealed class SmsProviderSetting : AggregateRoot<long>
{
    private SmsProviderSetting() { }

    public string Provider { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public bool IsVisible { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SmsProviderSetting Create(long id, string provider, string displayName, bool isEnabled, bool isVisible, int sortOrder)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(displayName) || sortOrder < 0)
            throw new DomainException("Invalid SMS provider setting.");

        return new SmsProviderSetting
        {
            Id = id, Provider = provider.Trim(), DisplayName = displayName.Trim(),
            IsEnabled = isEnabled, IsVisible = isVisible, SortOrder = sortOrder,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public void Configure(bool isEnabled, bool isVisible, int sortOrder)
    {
        if (sortOrder < 0) throw new DomainException("Sort order cannot be negative.");
        IsEnabled = isEnabled; IsVisible = isVisible; SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}