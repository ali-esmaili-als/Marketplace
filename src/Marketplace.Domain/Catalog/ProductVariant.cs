using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ProductVariant : Entity<long>
{
    private ProductVariant() { }
    public long ProductId { get; private set; }
    public string SKU { get; private set; } = null!;
    public string VariantKey { get; private set; } = null!;
    public byte[] VariantKeyHash { get; private set; } = Array.Empty<byte>();
    public long? PriceIRR { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static ProductVariant Create(long id,long productId,string sku,string variantKey,long? priceIrr=null)
    {
        if(id<=0||productId<=0||string.IsNullOrWhiteSpace(sku)||string.IsNullOrWhiteSpace(variantKey)||priceIrr<0)throw new DomainException("Invalid product variant.");
        return new ProductVariant{Id=id,ProductId=productId,SKU=sku.Trim(),VariantKey=variantKey.Trim(),PriceIRR=priceIrr,IsActive=true,CreatedAtUtc=DateTime.UtcNow};
    }
    public void SetPrice(long? priceIrr){if(priceIrr<0)throw new DomainException("Variant price cannot be negative.");PriceIRR=priceIrr;}
    public void SetSku(string sku){if(string.IsNullOrWhiteSpace(sku))throw new DomainException("SKU is required.");SKU=sku.Trim();}
    public void SetVariantKey(string key,byte[] hash){if(string.IsNullOrWhiteSpace(key)||hash.Length!=32)throw new DomainException("Invalid variant key.");VariantKey=key.Trim();VariantKeyHash=hash;}
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}