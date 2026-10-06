using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductWarranty : Entity<long>
{
    private ProductWarranty() { }
    public long ProductId { get; private set; }
    public long WarrantyId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public static ProductWarranty Create(long id,long productId,long warrantyId,int sortOrder,bool isDefault)
        => new() { Id=id, ProductId=productId, WarrantyId=warrantyId, SortOrder=sortOrder, IsDefault=isDefault, IsActive=true };
}
