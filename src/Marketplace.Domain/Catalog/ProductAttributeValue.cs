using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductAttributeValue : Entity<long>
{
    private ProductAttributeValue() { }
    public long ProductAttributeId { get; private set; }
    public string Value { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static ProductAttributeValue Create(long id, long attributeId, string value, string slug)
    {
        if (id <= 0 || attributeId <= 0 || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Invalid attribute value.");
        return new ProductAttributeValue { Id = id, ProductAttributeId = attributeId, Value = value.Trim(), Slug = slug.Trim(), IsActive = true };
    }
}
