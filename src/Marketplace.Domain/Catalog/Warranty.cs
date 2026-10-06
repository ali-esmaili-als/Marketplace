using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Warranty : AggregateRoot<long>
{
    private Warranty() { }
    public long WarrantyCategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public long PriceIRR { get; private set; }
    public bool IsFree { get; private set; }
    public bool IsActive { get; private set; }

    public static Warranty Create(long id,long warrantyCategoryId,string name,long priceIrr)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new DomainException("Warranty name is required.");
        if(priceIrr<0) throw new DomainException("Warranty price cannot be negative.");
        return new Warranty { Id=id, WarrantyCategoryId=warrantyCategoryId, Name=name.Trim(), PriceIRR=priceIrr, IsFree=priceIrr==0, IsActive=true };
    }
    public void ChangePrice(long priceIrr){ if(priceIrr<0) throw new DomainException("Warranty price cannot be negative."); PriceIRR=priceIrr; IsFree=priceIrr==0; }
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
