namespace Marketplace.Domain.Catalog;

public sealed class StorefrontMedia
{
    private StorefrontMedia() { }

    public long Id { get; private set; }
    public long StoreId { get; private set; }
    public long? ProductId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string Url { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string? AltText { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static StorefrontMedia Create(long id, long storeId, long? productId, string kind, string url, string contentType, string? altText, int sortOrder)
    {
        if (id <= 0 || storeId <= 0 || (productId.HasValue && productId.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(id));
        if (kind is not ("logo" or "banner" or "product"))
            throw new ArgumentException("Unsupported storefront media kind.", nameof(kind));
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500)
            throw new ArgumentException("A valid media URL is required.", nameof(url));
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new ArgumentException("Unsupported image content type.", nameof(contentType));
        if ((kind == "product") != productId.HasValue)
            throw new ArgumentException("Product media must reference a product, and store media must not.", nameof(productId));
        if (altText?.Length > 250 || sortOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sortOrder));

        return new StorefrontMedia
        {
            Id = id, StoreId = storeId, ProductId = productId, Kind = kind, Url = url,
            ContentType = contentType, AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
            SortOrder = sortOrder, IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Deactivate() => IsActive = false;
}
