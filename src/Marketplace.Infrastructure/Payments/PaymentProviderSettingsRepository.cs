using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Payments;
using Marketplace.Domain.Payments;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentProviderSettingsRepository(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db) : IPaymentProviderSettings
{
    public async Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default)
    {
        var settings = await db.PaymentProviderSettings
            .Where(x=>x.IsEnabled && x.IsVisible)
            .OrderBy(x=>x.SortOrder)
            .ToListAsync(ct);

        return settings
            .Where(x => PaymentProviderCapabilities.IsProtocolImplemented(x.Provider))
            .Select(x=>new PaymentProviderInfo(
                x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder,
                PaymentProviderCapabilities.IsProtocolImplemented(x.Provider)))
            .ToList();
    }

    public async Task<PaymentProviderInfo?> GetAsync(PaymentProviderCode provider,CancellationToken ct=default)
    {
        var setting = await db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,ct);
        return setting is null ? null : new PaymentProviderInfo(
            setting.Provider,setting.DisplayName,setting.IsEnabled,setting.IsVisible,setting.SortOrder,
            PaymentProviderCapabilities.IsProtocolImplemented(setting.Provider));
    }
}