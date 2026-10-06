using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductTypeWarrantyCategory : Entity<long>
{
    private ProductTypeWarrantyCategory() { }
    public long ProductTypeId { get; private set; }
    public long WarrantyCategoryId { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    public static ProductTypeWarrantyCategory Create(long id,long productTypeId,long warrantyCategoryId,bool isRequired,int sortOrder)
        => new() { Id=id, ProductTypeId=productTypeId, WarrantyCategoryId=warrantyCategoryId, IsRequired=isRequired, SortOrder=sortOrder };
}
