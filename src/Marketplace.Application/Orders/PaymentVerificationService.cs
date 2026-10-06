using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Orders;

public sealed record PaymentVerificationResult(bool Paid, string? Reference, string? Error);

public sealed class PaymentVerificationService
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentGateway _gateway;
    private readonly OrderLifecycleService _lifecycle;

    public PaymentVerificationService(IPaymentRepository payments, IPaymentGateway gateway, OrderLifecycleService lifecycle)
    {
        _payments=payments; _gateway=gateway; _lifecycle=lifecycle;
    }

    public async Task<PaymentVerificationResult> VerifyAsync(long paymentId, string authority, CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(authority)) throw new DomainException("Payment authority is required.");

        var payment=await _payments.GetAsync(paymentId,ct)??throw new DomainException("Payment not found.");
        if(!string.Equals(payment.Authority,authority,StringComparison.Ordinal))
            throw new DomainException("Payment authority does not match.");

        if(payment.Status==Marketplace.Domain.Payments.PaymentStatus.Succeeded)
            return new PaymentVerificationResult(true,payment.ReferenceNumber,null);

        if(payment.Status is Marketplace.Domain.Payments.PaymentStatus.Failed or Marketplace.Domain.Payments.PaymentStatus.Cancelled)
            return new PaymentVerificationResult(false,null,"Payment is no longer payable.");

        var result=await _gateway.VerifyAsync(authority,payment.AmountIRR,ct);
        if(!result.IsSuccessful)
            return new PaymentVerificationResult(false,result.Reference,result.Error);

        await _lifecycle.PaymentSucceededAsync(payment.OrderId,result.Reference ?? authority,ct);
        return new PaymentVerificationResult(true,result.Reference ?? authority,null);
    }
}
