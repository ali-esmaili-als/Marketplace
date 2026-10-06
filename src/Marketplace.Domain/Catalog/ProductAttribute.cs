using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductAttribute : AggregateRoot<long>
{
    private ProductAttribute() { }
    public long StoreId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static ProductAttribute Create(long id, long storeId, string name, string slug)
    {
        if (id <= 0 || storeId <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Invalid product attribute.");
        return new ProductAttribute { Id = id, StoreId = storeId, Name = name.Trim(), Slug = slug.Trim(), IsActive = true };
    }
}
