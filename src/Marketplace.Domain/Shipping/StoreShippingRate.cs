using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class StoreShippingRate : AggregateRoot<long>
{
    private StoreShippingRate() { }

    public long StoreId { get; private set; }
    public long CityId { get; private set; }
    public long ShippingFeeIRR { get; private set; }
    public int MinDeliveryDays { get; private set; }
    public int MaxDeliveryDays { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static StoreShippingRate Create(long id, long storeId, long cityId, long feeIRR, int minDays, int maxDays, DateTime now)
    {
        if (id <= 0 || storeId <= 0 || cityId <= 0) throw new DomainException("Invalid shipping rate identifiers.");
        Validate(feeIRR, minDays, maxDays);
        return new StoreShippingRate { Id = id, StoreId = storeId, CityId = cityId, ShippingFeeIRR = feeIRR, MinDeliveryDays = minDays, MaxDeliveryDays = maxDays, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    public void Update(long feeIRR, int minDays, int maxDays, DateTime now)
    {
        Validate(feeIRR, minDays, maxDays);
        if (now < UpdatedAtUtc) throw new DomainException("Shipping rate update time cannot move backwards.");
        ShippingFeeIRR = feeIRR;
        MinDeliveryDays = minDays;
        MaxDeliveryDays = maxDays;
        UpdatedAtUtc = now;
    }

    private static void Validate(long feeIRR, int minDays, int maxDays)
    {
        if (feeIRR < 0) throw new DomainException("Shipping fee cannot be negative.");
        if (minDays < 0 || maxDays < minDays || maxDays > 365) throw new DomainException("Invalid delivery estimate.");
    }
}
