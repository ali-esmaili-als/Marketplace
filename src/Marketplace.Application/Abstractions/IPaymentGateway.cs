namespace Marketplace.Application.Abstractions;

public sealed record PaymentRedirect(string Provider,string Authority,string Url);

/// <param name="IsOutcomeDefinitive">False when the provider response cannot prove either success or failure. Such outcomes must not mutate payment state.</param>
public sealed record PaymentVerification(bool IsSuccessful,string? Reference,string? Error,bool IsOutcomeDefinitive=true);

public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentRedirect> CreatePaymentAsync(long paymentId,long orderId,long amountIRR,CancellationToken cancellationToken=default);
    Task<PaymentVerification> VerifyAsync(string authority,long amountIRR,CancellationToken cancellationToken=default);
    Task<bool> RefundAsync(string? paymentReference,long amountIRR,CancellationToken cancellationToken=default);
}