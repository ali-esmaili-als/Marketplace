using Marketplace.Domain.Common;
using Marketplace.Domain.Sellers;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class StoreThemeTests
{
    [Theory]
    [InlineData("classic")]
    [InlineData("minimal")]
    [InlineData("vibrant")]
    [InlineData(" VIBRANT ")]
    public void ConfigureTheme_AcceptsSupportedThemeCodes(string theme)
    {
        var store = Store.Create(1, 2, "Store", "store");

        store.ConfigureTheme(theme);

        Assert.Contains(store.ThemeCode, new[] { "classic", "minimal", "vibrant" });
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("classic?color=red")]
    public void ConfigureTheme_RejectsUnsupportedValues(string theme)
    {
        var store = Store.Create(1, 2, "Store", "store");
        Assert.Throws<DomainException>(() => store.ConfigureTheme(theme));
    }
}
