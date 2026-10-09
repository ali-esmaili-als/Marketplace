using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentProviderSettingsAdminRepository(
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    IUnitOfWork uow) : IPaymentProviderSettingsAdmin
{
    public async Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct=default)
    {
        var settings = await db.PaymentProviderSettings.OrderBy(x=>x.SortOrder).ToListAsync(ct);
        return settings.Select(x=>new PaymentProviderSettingDto(
            x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder,
            PaymentProviderConfigurationSanitizer.Redact(x.ConfigurationJson),
            PaymentProviderCapabilities.IsProtocolImplemented(x.Provider),
            PaymentProviderCapabilities.ReadinessMessage(x.Provider))).ToList();
    }

    public async Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default)
    {
        await uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var setting=await db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,token)
                ?? throw new DomainException("Payment provider setting not found.");

            // The UI receives masked secrets. Merge them with the stored credentials before
            // persisting so saving a label/order change cannot silently erase bank credentials.
            var mergedConfiguration = PaymentProviderConfigurationSanitizer.MergePreservingSecrets(
                setting.ConfigurationJson, configurationJson);

            if(isEnabled)
            {
                var otherEnabled=await db.PaymentProviderSettings
                    .Where(x=>x.Provider!=provider && x.IsEnabled)
                    .ToListAsync(token);

                foreach(var other in otherEnabled)
                    other.Configure(false,other.IsVisible,other.SortOrder,other.ConfigurationJson);
            }

            setting.Configure(isEnabled,isVisible,sortOrder,mergedConfiguration);
            await uow.SaveChangesAsync(token);
            return 0;
        },ct);
    }
}