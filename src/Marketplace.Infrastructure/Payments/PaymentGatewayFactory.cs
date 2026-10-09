using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentGatewayFactory(
    IPaymentProviderSettings settings,
    IHttpClientFactory httpClientFactory,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : IPaymentGatewayFactory
{
    public async Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default)
    {
        var providers = await settings.GetAvailableAsync(ct);
        return providers
            .Where(x => x.IsProtocolImplemented)
            .Where(x => !environment.IsProduction() || x.Provider != PaymentProviderCode.TestBank)
            .ToArray();
    }

    public Task<IPaymentGateway> GetForExistingPaymentAsync(PaymentProviderCode provider,CancellationToken ct=default)=>CreateAsync(provider,ct,false);
    public Task<IPaymentGateway> GetAsync(PaymentProviderCode provider,CancellationToken ct=default)=>CreateAsync(provider,ct,true);

    private async Task<IPaymentGateway> CreateAsync(PaymentProviderCode provider,CancellationToken ct,bool requireVisible)
    {
        if (provider == PaymentProviderCode.TestBank && environment.IsProduction())
            throw new DomainException("The test bank provider is only available outside Production.");

        if (!PaymentProviderCapabilities.IsProtocolImplemented(provider))
            throw new DomainException("This payment provider is not operational because its official bank protocol has not been implemented.");

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