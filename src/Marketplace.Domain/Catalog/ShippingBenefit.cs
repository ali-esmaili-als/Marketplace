using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class ShippingBenefit : AggregateRoot<long>
{
    private ShippingBenefit() { }
    public long? MinOrderAmountIRR { get; private set; }
    public long? MaxOrderAmountIRR { get; private set; }
    public int? ShippingMethodId { get; private set; }
    public int? CityId { get; private set; }
    public long? MaxBenefitAmountIRR { get; private set; }
    public bool IsActive { get; private set; }

    public static ShippingBenefit Create(long id,long? minOrderAmountIrr,long? maxOrderAmountIrr,int? shippingMethodId,int? cityId,long? maxBenefitAmountIrr)
    {
        if(minOrderAmountIrr is < 0 || maxOrderAmountIrr is < 0 || maxBenefitAmountIrr is < 0) throw new DomainException("Shipping benefit amounts cannot be negative.");
        if(minOrderAmountIrr.HasValue && maxOrderAmountIrr.HasValue && maxOrderAmountIrr<minOrderAmountIrr) throw new DomainException("Maximum order amount cannot be lower than minimum.");
        return new ShippingBenefit { Id=id, MinOrderAmountIRR=minOrderAmountIrr, MaxOrderAmountIRR=maxOrderAmountIrr, ShippingMethodId=shippingMethodId, CityId=cityId, MaxBenefitAmountIRR=maxBenefitAmountIrr, IsActive=true };
    }
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
