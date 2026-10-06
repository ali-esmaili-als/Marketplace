using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Sellers;

public sealed class SellerManagementService(
    ISellerManagementRepository repo,
    IIdentityRepository identity,
    IShippingRepository shipping,
    IUnitOfWork uow,
    IIdGenerator ids)
{
    public async Task<long> ApplyAsync(long userId, CancellationToken ct = default)
    {
        var user = await identity.GetUserByIdAsync(userId, ct) ?? throw new DomainException("User not found.");
        if (!user.IsActive) throw new DomainException("User is inactive.");
        if (await repo.GetSellerByUserIdAsync(userId, ct) is not null)
            throw new DomainException("User is already registered as a seller.");

        var seller = Seller.Create(await ids.NextAsync(ct), userId);
        repo.AddSeller(seller);

        var role = await identity.GetRoleByNameAsync("Seller", ct)
            ?? throw new DomainException("Seller role is not configured.");
        identity.AddUserRoleAssignment(UserRoleAssignment.Create(await ids.NextAsync(ct), user.Id, role.Id));

        foreach (var code in new[] { "Seller.Shipping.Configure", "Seller.Settlement.Request", "Seller.Campaign.Manage", "Seller.Coupon.Manage", "Order.ReadOwn", "Seller.Catalog.Manage" })
        {
            var rule = await identity.GetRuleByCodeAsync(code, ct)
                ?? throw new DomainException($"Rule {code} is not configured.");
            identity.AddUserRule(UserRule.Create(await ids.NextAsync(ct), user.Id, rule.Id));
        }

        repo.AddBalance(SellerBalance.Create(await ids.NextAsync(ct), seller.Id));
        await uow.SaveChangesAsync(ct);
        return seller.Id;
    }

    public async Task ConfigureStoreLimitAsync(long sellerId, int maxStoreCount, CancellationToken ct = default)
    {
        var seller = await repo.GetSellerAsync(sellerId, ct) ?? throw new DomainException("Seller not found.");
        var currentCount = await repo.GetStoreCountAsync(sellerId, ct);
        if (maxStoreCount < currentCount) throw new DomainException("Store limit cannot be lower than current store count.");
        seller.ConfigureStoreLimit(maxStoreCount);
        await uow.SaveChangesAsync(ct);
    }

    public async Task SuspendAsync(long sellerId,CancellationToken ct=default){var seller=await repo.GetSellerAsync(sellerId,ct)??throw new DomainException("Seller not found.");seller.Suspend();await uow.SaveChangesAsync(ct);}
    public async Task RejectAsync(long sellerId,CancellationToken ct=default){var seller=await repo.GetSellerAsync(sellerId,ct)??throw new DomainException("Seller not found.");seller.Reject();await uow.SaveChangesAsync(ct);}
    public async Task ConfigureCommissionAsync(long sellerId,int rateBasisPoints,long minimumCommissionIrr,CancellationToken ct=default){var seller=await repo.GetSellerAsync(sellerId,ct)??throw new DomainException("Seller not found.");seller.ConfigureCommission(rateBasisPoints,minimumCommissionIrr);await uow.SaveChangesAsync(ct);}
    public async Task ActivateAsync(long sellerId, CancellationToken ct = default)
    {
        var seller = await repo.GetSellerAsync(sellerId, ct) ?? throw new DomainException("Seller not found.");
        seller.Activate();
        await uow.SaveChangesAsync(ct);
    }

    public async Task<long> CreateStoreAsync(long userId, string name, string slug, string? description, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var count = await repo.GetStoreCountAsync(seller.Id, ct);
        if (count >= seller.MaxStoreCount)
            throw new DomainException("Your store limit has been reached.");

        if (await repo.StoreSlugExistsAsync(seller.Id, slug, null, ct))
            throw new DomainException("Store slug already exists.");

        var store = Store.Create(await ids.NextAsync(ct), seller.Id, name, slug);
        if (!string.IsNullOrWhiteSpace(description)) store.SetDescription(description);
        repo.AddStore(store);
        await uow.SaveChangesAsync(ct);
        return store.Id;
    }

    public async Task UpdateStoreAsync(long userId, long storeId, string name, string slug, string? description, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var store = await repo.GetStoreForSellerAsync(storeId, seller.Id, ct) ?? throw new DomainException("Store not found.");
        if (await repo.StoreSlugExistsAsync(seller.Id, slug, store.Id, ct))
            throw new DomainException("Store slug already exists.");
        store.Rename(name, slug);
        store.SetDescription(description);
        await uow.SaveChangesAsync(ct);
    }

    public async Task ActivateStoreAsync(long userId, long storeId, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var store = await repo.GetStoreForSellerAsync(storeId, seller.Id, ct) ?? throw new DomainException("Store not found.");
        store.Activate();
        await uow.SaveChangesAsync(ct);
    }

    public async Task CloseStoreAsync(long userId, long storeId, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var store = await repo.GetStoreForSellerAsync(storeId, seller.Id, ct) ?? throw new DomainException("Store not found.");
        store.Close();
        await uow.SaveChangesAsync(ct);
    }

    public async Task<List<Store>> GetMyStoresAsync(long userId, CancellationToken ct = default)
    {
        var sellerId = await GetSellerIdAsync(userId, ct);
        return await repo.GetStoresAsync(sellerId, ct);
    }

    public async Task<long> AddBankAccountAsync(long userId, string bankName, string iban, string holderName, bool makeDefault, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var accounts = await repo.GetBankAccountsAsync(seller.Id, ct);
        if (accounts.Any(x => x.Iban == iban.Trim().Replace(" ", "").ToUpperInvariant()))
            throw new DomainException("IBAN already exists.");

        if (makeDefault) foreach (var account in accounts) account.UnsetDefault();

        var accountNew = SellerBankAccount.Create(await ids.NextAsync(ct), seller.Id, bankName, iban, holderName);
        if (makeDefault || accounts.Count == 0) accountNew.SetDefault();
        repo.AddBankAccount(accountNew);
        await uow.SaveChangesAsync(ct);
        return accountNew.Id;
    }

    public async Task SetDefaultBankAccountAsync(long userId, long accountId, CancellationToken ct = default)
    {
        var seller = await GetActiveSellerByUserAsync(userId, ct);
        var account = await repo.GetBankAccountAsync(seller.Id, accountId, ct) ?? throw new DomainException("Bank account not found.");
        foreach (var item in await repo.GetBankAccountsAsync(seller.Id, ct)) item.UnsetDefault();
        account.SetDefault();
        await uow.SaveChangesAsync(ct);
    }

    public async Task<List<SellerBankAccount>> GetMyBankAccountsAsync(long userId, CancellationToken ct = default)
    {
        var sellerId = await GetSellerIdAsync(userId, ct);
        return await repo.GetBankAccountsAsync(sellerId, ct);
    }

    private async Task<Seller> GetActiveSellerByUserAsync(long userId, CancellationToken ct)
    {
        var seller = await repo.GetSellerByUserIdAsync(userId, ct) ?? throw new DomainException("Seller profile not found.");
        if (seller.Status != SellerStatus.Active) throw new DomainException("Seller is not active.");
        return seller;
    }

    private async Task<long> GetSellerIdAsync(long userId, CancellationToken ct)
        => (await repo.GetSellerByUserIdAsync(userId, ct) ?? throw new DomainException("Seller profile not found.")).Id;
}