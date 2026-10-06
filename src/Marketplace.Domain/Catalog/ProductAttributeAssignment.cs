using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductAttributeAssignment : Entity<long>
{
    private ProductAttributeAssignment() { }
    public long ProductId { get; private set; }
    public long ProductAttributeId { get; private set; }

    public static ProductAttributeAssignment Create(long id, long productId, long attributeId)
    {
        if (id <= 0 || productId <= 0 || attributeId <= 0) throw new DomainException("Invalid product attribute assignment.");
        return new ProductAttributeAssignment { Id = id, ProductId = productId, ProductAttributeId = attributeId };
    }
}
