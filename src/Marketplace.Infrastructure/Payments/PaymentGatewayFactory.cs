using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentGatewayFactory(
    IPaymentProviderSettings settings,
    IHttpClientFactory httpClientFactory,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : IPaymentGatewayFactory
{
    public Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default)
        => settings.GetAvailableAsync(ct);

    public Task<IPaymentGateway> GetForExistingPaymentAsync(PaymentProviderCode provider,CancellationToken ct=default)=>CreateAsync(provider,ct,false);
    public Task<IPaymentGateway> GetAsync(PaymentProviderCode provider,CancellationToken ct=default)=>CreateAsync(provider,ct,true);

    private async Task<IPaymentGateway> CreateAsync(PaymentProviderCode provider,CancellationToken ct,bool requireVisible)
    {
        var setting=await db.PaymentProviderSettings.SingleOrDefaultAsync(x=>x.Provider==provider,ct)
            ?? throw new DomainException("Payment provider is not configured.");

        if(!setting.IsEnabled || (requireVisible && !setting.IsVisible))
            throw new DomainException("Payment provider is not currently available.");

        var config=BankGatewayAdapterBase.Parse(setting.ConfigurationJson);
        return provider switch
        {
            PaymentProviderCode.TestBank=>new TestBankPaymentGateway(configuration["Payment:TestReturnBaseUrl"] ?? (httpContextAccessor.HttpContext is { } context ? $"{context.Request.Scheme}://{context.Request.Host}" : null)),
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