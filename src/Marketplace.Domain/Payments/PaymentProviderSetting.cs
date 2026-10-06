using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public sealed class PaymentProviderSetting : AggregateRoot<long>
{
    private PaymentProviderSetting() { }

    public PaymentProviderCode Provider { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public bool IsVisible { get; private set; }
    public int SortOrder { get; private set; }
    public string ConfigurationJson { get; private set; } = "{}";
    public DateTime UpdatedAtUtc { get; private set; }

    public static PaymentProviderSetting Create(long id,PaymentProviderCode provider,string displayName,
        bool isEnabled,bool isVisible,int sortOrder,string configurationJson)
    {
        if(id<=0 || string.IsNullOrWhiteSpace(displayName) || sortOrder<0)
            throw new DomainException("Invalid payment provider setting.");
        return new PaymentProviderSetting
        {
            Id=id,Provider=provider,DisplayName=displayName.Trim(),IsEnabled=isEnabled,
            IsVisible=isVisible,SortOrder=sortOrder,ConfigurationJson=string.IsNullOrWhiteSpace(configurationJson)?"{}":configurationJson,
            UpdatedAtUtc=DateTime.UtcNow
        };
    }

    public void Configure(bool isEnabled,bool isVisible,int sortOrder,string configurationJson)
    {
        if(sortOrder<0) throw new DomainException("Sort order cannot be negative.");
        IsEnabled=isEnabled; IsVisible=isVisible; SortOrder=sortOrder;
        ConfigurationJson=string.IsNullOrWhiteSpace(configurationJson)?"{}":configurationJson;
        UpdatedAtUtc=DateTime.UtcNow;
    }

    public bool CanShow => IsEnabled && IsVisible;
}