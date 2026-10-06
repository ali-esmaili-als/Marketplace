using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentGatewayFactory(
    IPaymentProviderSettings settings,
    IHttpClientFactory httpClientFactory,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db) : IPaymentGatewayFactory
{
    public Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default)
        => settings.GetAvailableAsync(ct);

    public async Task<IPaymentGateway> GetAsync(PaymentProviderCode provider,CancellationToken ct=default)
    {
        var setting=await db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,ct)
            ?? throw new DomainException("Payment provider is not configured.");

        if(!setting.IsEnabled || !setting.IsVisible)
            throw new DomainException("Payment provider is not currently available.");

        var config=BankGatewayAdapterBase.Parse(setting.ConfigurationJson);
        return provider switch
        {
            PaymentProviderCode.Melli=>new MelliGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Parsian=>new ParsianGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Pasargad=>new PasargadGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Mellat=>new MellatGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Sepah=>new SepahGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Tejarat=>new TejaratGatewayAdapter(httpClientFactory,config),
            PaymentProviderCode.Saman=>new SamanGatewayAdapter(httpClientFactory,config),
            _=>throw new DomainException("Unsupported payment provider.")
        };
    }
}