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
        => await db.PaymentProviderSettings.OrderBy(x=>x.SortOrder)
            .Select(x=>new PaymentProviderSettingDto(x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder,x.ConfigurationJson))
            .ToListAsync(ct);

    public async Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default)
    {
        await uow.ExecuteInTransactionAsync(async token =>
        {
            var setting=await db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,token)
                ?? throw new DomainException("Payment provider setting not found.");
            setting.Configure(isEnabled,isVisible,sortOrder,configurationJson);
            await uow.SaveChangesAsync(token);
            return 0;
        },ct);
    }
}