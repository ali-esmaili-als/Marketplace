using Marketplace.Domain.Payments;

namespace Marketplace.Application.Abstractions;

public interface IPaymentGatewayFactory
{
    Task<IPaymentGateway> GetForExistingPaymentAsync(PaymentProviderCode provider,CancellationToken ct=default);
    Task<IPaymentGateway> GetAsync(PaymentProviderCode provider,CancellationToken ct=default);
    Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default);
}