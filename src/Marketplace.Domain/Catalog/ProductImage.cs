using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductImage : Entity<long>
{
    private ProductImage() { }
    public long ProductId { get; private set; }
    public long? VariantId { get; private set; }
    public string Url { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    public static ProductImage Create(long id, long productId, long? variantId, string url, int sortOrder, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new DomainException("Image URL is required.");
        return new ProductImage { Id=id, ProductId=productId, VariantId=variantId, Url=url.Trim(), SortOrder=sortOrder, IsPrimary=isPrimary };
    }
}
