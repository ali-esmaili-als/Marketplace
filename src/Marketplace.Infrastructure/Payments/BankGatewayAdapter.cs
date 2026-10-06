using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Infrastructure.Payments;

public abstract class BankGatewayAdapterBase : IPaymentGateway
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly BankGatewayConfiguration Configuration;

    protected BankGatewayAdapterBase(IHttpClientFactory httpClientFactory,BankGatewayConfiguration configuration)
    {
        HttpClientFactory=httpClientFactory; Configuration=configuration;
    }

    public abstract string ProviderName { get; }
    protected abstract PaymentProviderCode ProviderCode { get; }

    public virtual Task<PaymentRedirect> CreatePaymentAsync(long paymentId,long orderId,long amountIRR,CancellationToken ct=default)
        => throw new DomainException($"{ProviderName} gateway protocol adapter is not configured. Configure the official bank protocol before enabling this provider.");

    public virtual Task<PaymentVerification> VerifyAsync(string authority,long amountIRR,CancellationToken ct=default)
        => throw new DomainException($"{ProviderName} gateway protocol adapter is not configured. Configure the official bank protocol before enabling this provider.");

    public virtual Task<bool> RefundAsync(string? paymentReference,long amountIRR,CancellationToken ct=default)
        => throw new DomainException($"{ProviderName} gateway refund protocol adapter is not configured.");

    protected static BankGatewayConfiguration Parse(string json)
        => JsonSerializer.Deserialize<BankGatewayConfiguration>(json) ?? throw new DomainException("Invalid bank gateway configuration.");
}

public sealed class MelliGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Melli";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Melli;
}
public sealed class ParsianGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Parsian";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Parsian;
}
public sealed class PasargadGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Pasargad";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Pasargad;
}
public sealed class MellatGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Mellat";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Mellat;
}
public sealed class SepahGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Sepah";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Sepah;
}
public sealed class TejaratGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Tejarat";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Tejarat;
}
public sealed class SamanGatewayAdapter(IHttpClientFactory f,BankGatewayConfiguration c) : BankGatewayAdapterBase(f,c)
{
    public override string ProviderName=>"Saman";
    protected override PaymentProviderCode ProviderCode=>PaymentProviderCode.Saman;
}