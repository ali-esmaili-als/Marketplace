using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class StoreShippingCity : Entity<long>
{
    private StoreShippingCity() { }

    public long StoreId { get; private set; }
    public long CityId { get; private set; }
    public DeliveryCity City { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    public static StoreShippingCity Create(long id, long storeId, long cityId)
    {
        if (id <= 0 || storeId <= 0 || cityId <= 0)
            throw new DomainException("Invalid store shipping city identifiers.");

        return new StoreShippingCity
        {
            Id = id,
            StoreId = storeId,
            CityId = cityId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}