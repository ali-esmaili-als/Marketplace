using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Warranty : AggregateRoot<long>
{
    private Warranty() { }
    public long StoreId { get; private set; }
    public string Name { get; private set; } = null!;
    public long PriceIRR { get; private set; }
    public bool IsActive { get; private set; }

    public static Warranty Create(long id, long storeId, string name, long priceIrr = 0)
    {
        if (id <= 0 || storeId <= 0 || string.IsNullOrWhiteSpace(name) || priceIrr < 0)
            throw new DomainException("Invalid warranty.");
        return new Warranty { Id = id, StoreId = storeId, Name = name.Trim(), PriceIRR = priceIrr, IsActive = true };
    }
    public void SetPrice(long priceIrr) { if (priceIrr < 0) throw new DomainException("Warranty price cannot be negative."); PriceIRR = priceIrr; }
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
