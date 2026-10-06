using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Shipping;

namespace Marketplace.Application.Shipping;

public sealed class ShippingCoverageService
{
    private readonly IShippingRepository _shipping;
    private readonly IUnitOfWork _uow;
    private readonly ISellerManagementRepository _sellers;

    public ShippingCoverageService(IShippingRepository shipping, IUnitOfWork uow, ISellerManagementRepository sellers)
    {
        _shipping = shipping;
        _uow = uow;
        _sellers = sellers;
    }

    public Task<List<DeliveryCity>> GetCitiesAsync(CancellationToken ct = default)
        => _shipping.GetActiveCitiesAsync(ct);

    public Task<List<DeliveryCity>> GetStoreCitiesAsync(long storeId, CancellationToken ct = default)
        => _shipping.GetStoreCitiesAsync(storeId, ct);

    public async Task ConfigureStoreCitiesAsync(long userId, long storeId, IReadOnlyCollection<long> cityIds, CancellationToken ct = default)
    {
        if (userId <= 0 || storeId <= 0) throw new DomainException("Invalid store.");
        var seller = await _sellers.GetSellerByUserIdAsync(userId, ct) ?? throw new DomainException("Seller profile not found.");
        if (seller.Status != Marketplace.Domain.Sellers.SellerStatus.Active) throw new DomainException("Seller is not active.");
        if (!await _sellers.StoreBelongsToSellerAsync(storeId, seller.Id, ct)) throw new DomainException("Store not found.");

        var normalized = cityIds.Where(x => x > 0).Distinct().ToArray();
        foreach (var cityId in normalized)
        {
            var city = await _shipping.GetCityAsync(cityId, ct)
                ?? throw new DomainException($"City {cityId} was not found.");
            if (!city.IsActive)
                throw new DomainException($"City {city.Name} is not active.");
        }

        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            await _shipping.ReplaceStoreCitiesAsync(storeId, normalized, token);
            await _uow.SaveChangesAsync(token);
            return 0;
        }, ct);
    }

    public async Task EnsureStoreShipsToCityAsync(long storeId, long cityId, CancellationToken ct = default)
    {
        var city = await _shipping.GetCityAsync(cityId, ct)
            ?? throw new DomainException("Destination city was not found.");
        if (!city.IsActive)
            throw new DomainException("Destination city is not active.");

        if (!await _shipping.StoreShipsToCityAsync(storeId, cityId, ct))
            throw new DomainException($"This store does not ship to {city.Name}.");
    }
}