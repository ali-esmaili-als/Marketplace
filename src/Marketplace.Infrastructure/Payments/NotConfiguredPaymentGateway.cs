using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Payments;

public sealed class NotConfiguredPaymentGateway:IPaymentGateway
{
    public string ProviderName=>"NOT_CONFIGURED";
    public Task<PaymentRedirect> CreatePaymentAsync(long paymentId,long orderId,long amountIRR,CancellationToken cancellationToken=default)
        =>throw new InvalidOperationException("Payment gateway is not configured.");
    public Task<PaymentVerification> VerifyAsync(string authority,long amountIRR,CancellationToken cancellationToken=default)
        =>throw new InvalidOperationException("Payment gateway is not configured.");
    public Task<PaymentRefundResult> RefundAsync(string? paymentReference,long amountIRR,CancellationToken cancellationToken=default)
        =>throw new InvalidOperationException("Payment gateway is not configured.");
}