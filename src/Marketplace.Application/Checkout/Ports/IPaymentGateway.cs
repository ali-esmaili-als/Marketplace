namespace Marketplace.Application.Checkout.Ports;

public interface IPaymentGateway
{
    Task<PaymentRedirectResult> CreatePaymentAsync(PaymentRedirectRequest request, CancellationToken cancellationToken = default);
}

public sealed record PaymentRedirectRequest(long PaymentAttemptId, long AmountIRR, string CurrencyCode);
public sealed record PaymentRedirectResult(string RedirectUrl, string? GatewayReference);