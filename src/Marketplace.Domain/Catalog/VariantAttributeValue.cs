using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class VariantAttributeValue : Entity<long>
{
    private VariantAttributeValue() { }
    public long ProductVariantId { get; private set; }
    public long ProductAttributeValueId { get; private set; }

    public static VariantAttributeValue Create(long id, long variantId, long attributeValueId)
    {
        if (id <= 0 || variantId <= 0 || attributeValueId <= 0) throw new DomainException("Invalid variant attribute value.");
        return new VariantAttributeValue { Id = id, ProductVariantId = variantId, ProductAttributeValueId = attributeValueId };
    }
}
