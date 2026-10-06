using Marketplace.Domain.Catalog;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Abstractions;

public sealed record CheckoutLineData(
    Product Product,
    ProductVariant Variant,
    InventoryItem Inventory,
    Warranty? Warranty);

public interface ICatalogRepository
{
    Task<CheckoutLineData?> GetCheckoutLineAsync(long variantId, long? warrantyId, CancellationToken cancellationToken = default);
    Task<Store?> GetStoreAsync(long storeId, CancellationToken cancellationToken = default);
}
