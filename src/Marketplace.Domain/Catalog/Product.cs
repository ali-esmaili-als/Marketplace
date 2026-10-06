using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Product : AggregateRoot<long>
{
    private Product() { }

    public long StoreId { get; private set; }
    public long CategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public long BasePriceIRR { get; private set; }
    public ProductStatus Status { get; private set; }
    public bool HasVariants { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Product Create(long id, long storeId, long categoryId, string name, string slug, long basePriceIrr, bool hasVariants)
    {
        if (id <= 0 || storeId <= 0 || categoryId <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug) || basePriceIrr < 0)
            throw new DomainException("Invalid product.");

        return new Product
        {
            Id = id, StoreId = storeId, CategoryId = categoryId, Name = name.Trim(),
            Slug = slug.Trim(), BasePriceIRR = basePriceIrr, HasVariants = hasVariants,
            Status = ProductStatus.Draft, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Rename(string name,string slug){if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(slug))throw new DomainException("Product name and slug are required.");Name=name.Trim();Slug=slug.Trim();}
    public void SetDescription(string? description) => Description = description?.Trim();
    public void SetBasePrice(long priceIrr) { if (priceIrr < 0) throw new DomainException("Price cannot be negative."); BasePriceIRR = priceIrr; }
    public void Activate() => Status = ProductStatus.Active;
    public void Deactivate() => Status = ProductStatus.Inactive;
    public void Archive() => Status = ProductStatus.Archived;
}
