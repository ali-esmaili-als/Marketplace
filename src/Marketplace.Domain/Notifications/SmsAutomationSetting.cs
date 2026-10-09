using Marketplace.Domain.Common;

namespace Marketplace.Domain.Notifications;

public sealed class SmsAutomationSetting : AggregateRoot<long>
{
    private SmsAutomationSetting() { }
    public bool AutomaticSmsEnabled { get; private set; }
    public bool LowStockSmsEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SmsAutomationSetting CreateDefault()
        => new() { Id = 1, AutomaticSmsEnabled = false, LowStockSmsEnabled = false, UpdatedAtUtc = DateTime.UtcNow };

    public void Configure(bool automaticSmsEnabled, bool lowStockSmsEnabled)
    {
        if (lowStockSmsEnabled && !automaticSmsEnabled)
            throw new DomainException("Low-stock SMS requires automatic SMS to be enabled.");
        AutomaticSmsEnabled = automaticSmsEnabled;
        LowStockSmsEnabled = lowStockSmsEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
