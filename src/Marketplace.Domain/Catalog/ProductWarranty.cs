using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductWarranty : Entity<long>
{
    private ProductWarranty() { }
    public long ProductId { get; private set; }
    public long WarrantyId { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public static ProductWarranty Create(long id, long productId, long warrantyId, bool isDefault = false)
    {
        if (id <= 0 || productId <= 0 || warrantyId <= 0) throw new DomainException("Invalid product warranty.");
        return new ProductWarranty { Id = id, ProductId = productId, WarrantyId = warrantyId, IsDefault = isDefault, IsActive = true };
    }
}
