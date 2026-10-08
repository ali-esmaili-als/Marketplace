using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Notifications;

public sealed class SmsProviderSettingsRepository(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db) : ISmsProviderSettings
{
    public async Task<IReadOnlyList<SmsProviderInfo>> GetAllAsync(CancellationToken ct = default)
        => await db.SmsProviderSettings.OrderBy(x => x.SortOrder)
            .Select(x => new SmsProviderInfo(x.Provider, x.DisplayName, x.IsEnabled, x.IsVisible, x.SortOrder))
            .ToListAsync(ct);

    public Task<SmsProviderInfo?> GetSelectedAsync(CancellationToken ct = default)
        => db.SmsProviderSettings.Where(x => x.IsEnabled && x.IsVisible).OrderBy(x => x.SortOrder)
            .Select(x => new SmsProviderInfo(x.Provider, x.DisplayName, x.IsEnabled, x.IsVisible, x.SortOrder))
            .FirstOrDefaultAsync(ct);
}