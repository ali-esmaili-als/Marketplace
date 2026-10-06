using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductVariant : Entity<long>
{
    private ProductVariant() { }
    public long ProductId { get; private set; }
    public string? Sku { get; private set; }
    public long? PriceIRR { get; private set; }
    public int StockQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public bool IsActive { get; private set; }
    public string VariantKey { get; private set; } = null!;
    public IReadOnlyCollection<ProductVariantAttributeValue> AttributeValues => _attributeValues.AsReadOnly();
    private readonly List<ProductVariantAttributeValue> _attributeValues=[];

    public static ProductVariant Create(long id, long productId, string variantKey, string? sku, long? priceIrr)
    {
        if (string.IsNullOrWhiteSpace(variantKey)) throw new DomainException("Variant key is required.");
        if (priceIrr is < 0) throw new DomainException("Variant price cannot be negative.");
        return new ProductVariant { Id=id, ProductId=productId, VariantKey=variantKey.Trim(), Sku=sku?.Trim(), PriceIRR=priceIrr, IsActive=true };
    }

    public void SetPrice(long? priceIrr) { if (priceIrr is < 0) throw new DomainException("Variant price cannot be negative."); PriceIRR=priceIrr; }
    public void AddAttributeValue(ProductVariantAttributeValue value) => _attributeValues.Add(value);
    public void Activate() => IsActive=true;
    public void Deactivate() => IsActive=false;
}
