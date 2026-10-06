using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class WarrantyCategory : AggregateRoot<long>
{
    private WarrantyCategory() { }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public static WarrantyCategory Create(long id,string name)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new DomainException("Warranty category name is required.");
        return new WarrantyCategory { Id=id, Name=name.Trim(), IsActive=true };
    }
    public void Rename(string name){ if(string.IsNullOrWhiteSpace(name)) throw new DomainException("Warranty category name is required."); Name=name.Trim(); }
    public void Activate()=>IsActive=true;
    public void Deactivate()=>IsActive=false;
}
