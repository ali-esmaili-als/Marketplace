using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductTypeAttributeValue : Entity<long>
{
    private ProductTypeAttributeValue() { }
    public long ProductTypeId { get; private set; }
    public long AttributeId { get; private set; }
    public long AttributeValueId { get; private set; }

    public static ProductTypeAttributeValue Create(long id, long productTypeId, long attributeId, long attributeValueId)
        => new() { Id=id, ProductTypeId=productTypeId, AttributeId=attributeId, AttributeValueId=attributeValueId };
}
