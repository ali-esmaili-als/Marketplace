using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Payments;

public sealed record PaymentProviderSettingDto(PaymentProviderCode Provider,string DisplayName,bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);

public sealed class PaymentProviderSettingsService
{
    private readonly Marketplace.Application.Abstractions.IPaymentProviderSettings _reader;
    private readonly Marketplace.Infrastructure.Persistence.MarketplaceDbContext _db;
    private readonly IUnitOfWork _uow;

    public PaymentProviderSettingsService(IPaymentProviderSettings reader,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,IUnitOfWork uow)
    {
        _reader=reader; _db=db; _uow=uow;
    }

    public async Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct=default)
        => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _db.PaymentProviderSettings.OrderBy(x=>x.SortOrder)
                .Select(x=>new PaymentProviderSettingDto(x.Provider,x.DisplayName,x.IsEnabled,x.IsVisible,x.SortOrder,x.ConfigurationJson)),ct);

    public async Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var setting=await _db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,token)
                ?? throw new DomainException("Payment provider setting not found.");
            setting.Configure(isEnabled,isVisible,sortOrder,configurationJson);
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);
    }
}