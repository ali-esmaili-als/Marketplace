using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Product : AggregateRoot<long>
{
    private readonly List<long> _categoryIds = [];
    private readonly List<ProductAttributeValue> _attributeValues = [];
    private readonly List<ProductVariant> _variants = [];
    private readonly List<ProductImage> _images = [];

    private Product() { }
    public long StoreId { get; private set; }
    public long ProductTypeId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<long> CategoryIds => _categoryIds.AsReadOnly();
    public IReadOnlyCollection<ProductAttributeValue> AttributeValues => _attributeValues.AsReadOnly();
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    public static Product Create(long id, long storeId, long productTypeId, string name, string slug, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) throw new DomainException("Product name and slug are required.");
        var now=DateTime.UtcNow;
        return new Product { Id=id, StoreId=storeId, ProductTypeId=productTypeId, Name=name.Trim(), Slug=slug.Trim().ToLowerInvariant(), Description=description, IsActive=true, CreatedAtUtc=now, UpdatedAtUtc=now };
    }

    public void Update(string name, string slug, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) throw new DomainException("Product name and slug are required.");
        Name=name.Trim(); Slug=slug.Trim().ToLowerInvariant(); Description=description; Touch();
    }

    public void AddCategory(long categoryId) { if (!_categoryIds.Contains(categoryId)) _categoryIds.Add(categoryId); Touch(); }
    public void RemoveCategory(long categoryId) { _categoryIds.Remove(categoryId); Touch(); }
    public void AddAttributeValue(ProductAttributeValue value) { _attributeValues.Add(value); Touch(); }
    public void AddVariant(ProductVariant variant) { _variants.Add(variant); Touch(); }
    public void AddImage(ProductImage image) { _images.Add(image); Touch(); }
    public void Activate() { IsActive=true; Touch(); }
    public void Deactivate() { IsActive=false; Touch(); }
    private void Touch() => UpdatedAtUtc=DateTime.UtcNow;
}
