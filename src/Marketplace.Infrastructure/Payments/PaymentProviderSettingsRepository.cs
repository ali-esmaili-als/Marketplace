using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Payments;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentProviderSettingsRepository(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db) : IPaymentProviderSettings
{
    public async Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default)
        => await db.PaymentProviderSettings
            .Where(x=>x.IsEnabled && x.IsVisible)
            .OrderBy(x=>x.SortOrder)
            .Select(x=>new PaymentProviderInfo(x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder))
            .ToListAsync(ct);

    public async Task<PaymentProviderInfo?> GetAsync(PaymentProviderCode provider,CancellationToken ct=default)
        => await db.PaymentProviderSettings.Where(x=>x.Provider==provider)
            .Select(x=>new PaymentProviderInfo(x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder))
            .SingleOrDefaultAsync(ct);
}