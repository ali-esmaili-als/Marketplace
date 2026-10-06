namespace Marketplace.Application.Abstractions;

public sealed record PaymentRedirect(string Provider,string Authority,string Url);

public sealed record PaymentVerification(bool IsSuccessful,string? Reference,string? Error);

public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentRedirect> CreatePaymentAsync(long paymentId,long orderId,long amountIRR,CancellationToken cancellationToken=default);
    Task<PaymentVerification> VerifyAsync(string authority,long amountIRR,CancellationToken cancellationToken=default);
    Task<bool> RefundAsync(string? paymentReference,long amountIRR,CancellationToken cancellationToken=default);
}