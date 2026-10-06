using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductTypeAttribute : Entity<long>
{
    private ProductTypeAttribute() { }
    public long ProductTypeId { get; private set; }
    public long AttributeId { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsVariantAttribute { get; private set; }
    public int SortOrder { get; private set; }

    public static ProductTypeAttribute Create(long id, long productTypeId, long attributeId, bool isRequired, bool isVariantAttribute, int sortOrder)
        => new() { Id=id, ProductTypeId=productTypeId, AttributeId=attributeId, IsRequired=isRequired, IsVariantAttribute=isVariantAttribute, SortOrder=sortOrder };
}
