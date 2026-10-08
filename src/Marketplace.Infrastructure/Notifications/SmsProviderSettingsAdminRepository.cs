using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Notifications;
using Marketplace.Domain.Common;

namespace Marketplace.Infrastructure.Notifications;

public sealed class SmsProviderSettingsAdminRepository(
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, IUnitOfWork uow) : ISmsProviderSettingsAdmin
{
    public async Task<IReadOnlyList<SmsProviderSettingDto>> GetAllAsync(CancellationToken ct = default)
        => await db.SmsProviderSettings.OrderBy(x => x.SortOrder)
            .Select(x => new SmsProviderSettingDto(x.Provider, x.DisplayName, x.IsEnabled, x.IsVisible, x.SortOrder))
            .ToListAsync(ct);

    public async Task ConfigureAsync(string provider, bool isEnabled, bool isVisible, int sortOrder, CancellationToken ct = default)
    {
        await uow.ExecuteInTransactionAsync(async token =>
        {
            var setting = await db.SmsProviderSettings.SingleOrDefaultAsync(x => x.Provider == provider, token)
                ?? throw new DomainException("SMS provider setting not found.");

            if (isEnabled)
            {
                var others = await db.SmsProviderSettings.Where(x => x.Provider != provider && x.IsEnabled).ToListAsync(token);
                foreach (var other in others) other.Configure(false, other.IsVisible, other.SortOrder);
            }

            setting.Configure(isEnabled, isVisible, sortOrder);
            await uow.SaveChangesAsync(token);
            return 0;
        }, ct);
    }
}