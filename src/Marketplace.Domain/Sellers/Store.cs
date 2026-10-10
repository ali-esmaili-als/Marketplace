using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class Store : AggregateRoot<long>
{
    private Store() { }

    public long SellerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string ThemeCode { get; private set; } = "classic";
    public string PaletteCode { get; private set; } = "ocean";
    public string ThemePrimaryColor { get; private set; } = "#3159c9";
    public string ThemeSecondaryColor { get; private set; } = "#f3f7ff";
    public string ThemeBackgroundColor { get; private set; } = "#ffffff";
    public string ThemeTextColor { get; private set; } = "#232b49";
    public string ThemeFontCode { get; private set; } = "iran-yekan";
    public string ThemeCornerStyle { get; private set; } = "soft";
    public StoreStatus Status { get; private set; }
    public int CommissionRateBasisPoints { get; private set; }
    public long MinimumCommissionIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Store Create(long id, long sellerId, string name, string slug,
        int commissionRateBasisPoints = 0, long minimumCommissionIrr = 0)
    {
        if (id <= 0 || sellerId <= 0) throw new DomainException("Invalid store identifiers.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Store name and slug are required.");
        if (commissionRateBasisPoints is < 0 or > 10000)
            throw new DomainException("Commission rate must be between 0 and 10000 basis points.");
        if (minimumCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");

        return new Store
        {
            Id = id,
            SellerId = sellerId,
            Name = name.Trim(),
            Slug = slug.Trim(),
            Status = StoreStatus.Draft,
            CommissionRateBasisPoints = commissionRateBasisPoints,
            MinimumCommissionIRR = minimumCommissionIrr,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Activate() => Status = StoreStatus.Active;
    public void Suspend() => Status = StoreStatus.Suspended;
    public void Close() => Status = StoreStatus.Closed;

    public void Rename(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Store name and slug are required.");
        Name = name.Trim();
        Slug = slug.Trim();
    }

    public void SetDescription(string? description) => Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public void ConfigureTheme(string themeCode)
    {
        var normalized = themeCode?.Trim().ToLowerInvariant();
        if (normalized is not ("classic" or "minimal" or "vibrant" or "editorial" or "boutique" or "magazine" or "grid" or "luxe" or "organic" or "tech" or "fashion" or "gallery" or "market" or "mono" or "pastel" or "bold" or "nordic" or "artisan" or "urban" or "elegant"))
            throw new DomainException("Unsupported store layout.");
        ThemeCode = normalized;
    }

    public void ConfigurePalette(string paletteCode)
    {
        var normalized = paletteCode?.Trim().ToLowerInvariant();
        if (normalized is not ("ocean" or "forest" or "sunset" or "rose" or "monochrome"))
            throw new DomainException("Unsupported store color palette.");
        PaletteCode = normalized;
    }

    public void ConfigureAppearance(string? primaryColor, string? secondaryColor, string? backgroundColor,
        string? textColor, string? fontCode, string? cornerStyle)
    {
        ThemePrimaryColor = NormalizeColor(primaryColor, ThemePrimaryColor, nameof(primaryColor));
        ThemeSecondaryColor = NormalizeColor(secondaryColor, ThemeSecondaryColor, nameof(secondaryColor));
        ThemeBackgroundColor = NormalizeColor(backgroundColor, ThemeBackgroundColor, nameof(backgroundColor));
        ThemeTextColor = NormalizeColor(textColor, ThemeTextColor, nameof(textColor));
        if (!string.IsNullOrWhiteSpace(fontCode))
        {
            var font = fontCode.Trim().ToLowerInvariant();
            if (font is not ("iran-yekan" or "system" or "serif" or "modern"))
                throw new DomainException("Unsupported store font.");
            ThemeFontCode = font;
        }
        if (!string.IsNullOrWhiteSpace(cornerStyle))
        {
            var corners = cornerStyle.Trim().ToLowerInvariant();
            if (corners is not ("soft" or "square" or "round"))
                throw new DomainException("Unsupported store corner style.");
            ThemeCornerStyle = corners;
        }
    }

    private static string NormalizeColor(string? value, string current, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return current;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 7 || normalized[0] != '#' ||
            !System.Text.RegularExpressions.Regex.IsMatch(normalized[1..], "^[0-9a-f]{6}$"))
            throw new DomainException($"Invalid color value for {field}.");
        return normalized;
    }

    public void ConfigureCommission(int rateBasisPoints, long minimumCommissionIrr)
    {
        if (rateBasisPoints is < 0 or > 10000 || minimumCommissionIrr < 0)
            throw new DomainException("Invalid commission configuration.");
        CommissionRateBasisPoints = rateBasisPoints;
        MinimumCommissionIRR = minimumCommissionIrr;
    }
}
