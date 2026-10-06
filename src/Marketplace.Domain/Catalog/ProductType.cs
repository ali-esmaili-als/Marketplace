using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductType : AggregateRoot<long>
{
    private ProductType() { }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static ProductType Create(long id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Product type name is required.");
        return new ProductType { Id=id, Name=name.Trim(), IsActive=true };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Product type name is required.");
        Name=name.Trim();
    }
}
