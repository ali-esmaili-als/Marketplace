using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductVariantAttributeValue : Entity<long>
{
    private ProductVariantAttributeValue() { }
    public long ProductId { get; private set; }
    public long VariantId { get; private set; }
    public long AttributeId { get; private set; }
    public long AttributeValueId { get; private set; }

    public static ProductVariantAttributeValue Create(long id, long productId, long variantId, long attributeId, long attributeValueId)
        => new() { Id=id, ProductId=productId, VariantId=variantId, AttributeId=attributeId, AttributeValueId=attributeValueId };
}
