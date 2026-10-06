using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Campaign : AggregateRoot<long>
{
    private readonly List<long> _productIds=[];
    private readonly List<long> _variantIds=[];
    private readonly List<long> _shippingBenefitIds=[];
    private Campaign() { }
    public long StoreId { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<long> ProductIds=>_productIds.AsReadOnly();
    public IReadOnlyCollection<long> VariantIds=>_variantIds.AsReadOnly();
    public IReadOnlyCollection<long> ShippingBenefitIds=>_shippingBenefitIds.AsReadOnly();

    public static Campaign Create(long id,long storeId,string name,DateTime startAtUtc,DateTime endAtUtc)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new DomainException("Campaign name is required.");
        if(endAtUtc<=startAtUtc) throw new DomainException("Campaign end must be after start.");
        return new Campaign { Id=id, StoreId=storeId, Name=name.Trim(), StartAtUtc=startAtUtc, EndAtUtc=endAtUtc, IsActive=true };
    }
    public void AddProduct(long productId){if(!_productIds.Contains(productId))_productIds.Add(productId);}
    public void AddVariant(long variantId){if(!_variantIds.Contains(variantId))_variantIds.Add(variantId);}
    public void AddShippingBenefit(long benefitId){if(!_shippingBenefitIds.Contains(benefitId))_shippingBenefitIds.Add(benefitId);}
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
