using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class AttributeValue : Entity<long>
{
    private AttributeValue() { }
    public long AttributeId { get; private set; }
    public string Value { get; private set; } = null!;
    public string? Slug { get; private set; }
    public bool IsActive { get; private set; }

    public static AttributeValue Create(long id, long attributeId, string value, string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("Attribute value is required.");
        return new AttributeValue { Id=id, AttributeId=attributeId, Value=value.Trim(), Slug=string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant(), IsActive=true };
    }
}
