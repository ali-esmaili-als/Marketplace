using Marketplace.Domain.Common;

namespace Marketplace.Domain.Pricing;

public sealed class Campaign : AggregateRoot<long>
{
    private Campaign() { }
    public long StoreId { get; private set; }
    public string Name { get; private set; } = null!;
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static Campaign Create(long id,long storeId,string name,DiscountType type,decimal value,DateTime startsAtUtc,DateTime endsAtUtc)
    {
        if(id<=0||storeId<=0||string.IsNullOrWhiteSpace(name)||value<0||(type==DiscountType.Percentage&&value>100)||endsAtUtc<=startsAtUtc)
            throw new DomainException("Invalid campaign.");
        return new Campaign { Id=id,StoreId=storeId,Name=name.Trim(),DiscountType=type,DiscountValue=value,StartsAtUtc=startsAtUtc,EndsAtUtc=endsAtUtc,IsActive=true };
    }
    public bool IsRunning(DateTime nowUtc) => IsActive && nowUtc>=StartsAtUtc && nowUtc<EndsAtUtc;
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}