using Marketplace.Domain.Shipping;

namespace Marketplace.Application.Abstractions;

public interface IShippingRepository
{
    Task<DeliveryCity?> GetCityAsync(long cityId, CancellationToken cancellationToken = default);
    Task<List<DeliveryCity>> GetActiveCitiesAsync(CancellationToken cancellationToken = default);
    Task<List<DeliveryCity>> GetStoreCitiesAsync(long storeId, CancellationToken cancellationToken = default);
    Task<bool> StoreShipsToCityAsync(long storeId, long cityId, CancellationToken cancellationToken = default);
    Task<StoreShippingRate?> GetStoreShippingRateAsync(long storeId, long cityId, CancellationToken cancellationToken = default);
    Task<List<long>> GetStoreCityIdsAsync(long storeId, CancellationToken cancellationToken = default);
    Task ReplaceStoreCitiesAsync(long storeId, IReadOnlyCollection<long> cityIds, CancellationToken cancellationToken = default);
    void AddStoreShippingCity(StoreShippingCity item);
    void RemoveStoreShippingCities(IEnumerable<StoreShippingCity> items);
}