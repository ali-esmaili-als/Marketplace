using Marketplace.Domain.Finance;
using Marketplace.Domain.Sellers;
using Marketplace.Domain.Shipping;

namespace Marketplace.Application.Abstractions;

public interface ISellerManagementRepository
{
    Task<Seller?> GetSellerByUserIdAsync(long userId, CancellationToken ct = default);
    Task<Seller?> GetSellerAsync(long sellerId, CancellationToken ct = default);
    Task<int> GetStoreCountAsync(long sellerId, CancellationToken ct = default);
    Task<Store?> GetStoreAsync(long storeId, CancellationToken ct = default);
    Task<List<Store>> GetStoresAsync(long sellerId, CancellationToken ct = default);
    Task<Store?> GetStoreForSellerAsync(long storeId, long sellerId, CancellationToken ct = default);
    Task<SellerBankAccount?> GetBankAccountAsync(long sellerId, long accountId, CancellationToken ct = default);
    Task<List<SellerBankAccount>> GetBankAccountsAsync(long sellerId, CancellationToken ct = default);
    Task<SellerBalance?> GetBalanceAsync(long sellerId, CancellationToken ct = default);
    Task<bool> StoreBelongsToSellerAsync(long storeId, long sellerId, CancellationToken ct = default);
    Task<bool> StoreSlugExistsAsync(long sellerId, string slug, long? exceptStoreId = null, CancellationToken ct = default);
    void AddSeller(Seller seller);
    void AddStore(Store store);
    void AddBankAccount(SellerBankAccount account);
    void AddBalance(SellerBalance balance);
}