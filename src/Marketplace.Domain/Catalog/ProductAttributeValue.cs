using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductAttributeValue : Entity<long>
{
    private ProductAttributeValue() { }
    public long ProductId { get; private set; }
    public long AttributeId { get; private set; }
    public long AttributeValueId { get; private set; }
    public string? CustomValue { get; private set; }

    public static ProductAttributeValue Create(long id, long productId, long attributeId, long attributeValueId, string? customValue = null)
        => new() { Id=id, ProductId=productId, AttributeId=attributeId, AttributeValueId=attributeValueId, CustomValue=customValue };
}
