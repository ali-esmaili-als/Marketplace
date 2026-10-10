using Marketplace.Domain.Common;
using Marketplace.Domain.Sellers;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class StoreThemeTests
{
    private static readonly string[] Themes =
    [
        "classic", "minimal", "vibrant", "editorial", "boutique", "magazine", "grid", "luxe",
        "organic", "tech", "fashion", "gallery", "market", "mono", "pastel", "bold",
        "nordic", "artisan", "urban", "elegant"
    ];

    private static readonly string[] Palettes = ["ocean", "forest", "sunset", "rose", "monochrome"];

    [Theory]
    [MemberData(nameof(SupportedThemes))]
    public void ConfigureTheme_AcceptsEverySupportedTheme(string theme)
    {
        var store = Store.Create(1, 2, "Store", "store");
        store.ConfigureTheme($" {theme.ToUpperInvariant()} ");
        Assert.Equal(theme, store.ThemeCode);
    }

    public static IEnumerable<object[]> SupportedThemes() => Themes.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(SupportedPalettes))]
    public void ConfigurePalette_AcceptsEverySupportedPalette(string palette)
    {
        var store = Store.Create(1, 2, "Store", "store");
        store.ConfigurePalette($" {palette.ToUpperInvariant()} ");
        Assert.Equal(palette, store.PaletteCode);
    }

    public static IEnumerable<object[]> SupportedPalettes() => Palettes.Select(x => new object[] { x });

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("classic?color=red")]
    public void ConfigureTheme_RejectsUnsupportedValues(string theme)
    {
        var store = Store.Create(1, 2, "Store", "store");
        Assert.Throws<DomainException>(() => store.ConfigureTheme(theme));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("ocean;drop")]
    public void ConfigurePalette_RejectsUnsupportedValues(string palette)
    {
        var store = Store.Create(1, 2, "Store", "store");
        Assert.Throws<DomainException>(() => store.ConfigurePalette(palette));
    }
}
