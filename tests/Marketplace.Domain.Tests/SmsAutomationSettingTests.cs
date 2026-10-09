using Marketplace.Domain.Notifications;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SmsAutomationSettingTests
{
    [Fact]
    public void DefaultSettingsDisableAutomaticAndLowStockSms()
    {
        var setting = SmsAutomationSetting.CreateDefault();
        Assert.Equal(1, setting.Id);
        Assert.False(setting.AutomaticSmsEnabled);
        Assert.False(setting.LowStockSmsEnabled);
    }

    [Fact]
    public void LowStockSmsCannotBeEnabledWhenAutomaticSmsIsDisabled()
    {
        var setting = SmsAutomationSetting.CreateDefault();
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() => setting.Configure(false, true));
    }

    [Fact]
    public void AutomaticSmsCanBeDisabledAndDisablesLowStockSms()
    {
        var setting = SmsAutomationSetting.CreateDefault();
        setting.Configure(true, true);
        setting.Configure(false, false);
        Assert.False(setting.AutomaticSmsEnabled);
        Assert.False(setting.LowStockSmsEnabled);
    }
}
