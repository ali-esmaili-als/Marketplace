using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Orders;

public sealed record PaymentVerificationResult(bool Paid, string? Reference, string? Error);

public sealed class PaymentVerificationService
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentGateway _gateway;
    private readonly IUnitOfWork _uow;
    private readonly OrderLifecycleService _lifecycle;

    public PaymentVerificationService(IPaymentRepository payments,IPaymentGateway gateway,IUnitOfWork uow,OrderLifecycleService lifecycle)
    {
        _payments=payments; _gateway=gateway; _uow=uow; _lifecycle=lifecycle;
    }

    public async Task<PaymentVerificationResult> VerifyAsync(long paymentId,string authority,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(authority)) throw new DomainException("Payment authority is required.");

        var payment=await _payments.GetAsync(paymentId,ct)??throw new DomainException("Payment not found.");
        if(!string.Equals(payment.Authority,authority,StringComparison.Ordinal))
            throw new DomainException("Payment authority does not match.");

        if(payment.Status==PaymentStatus.Succeeded)
            return new PaymentVerificationResult(true,payment.ReferenceNumber,null);

        if(payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
            return new PaymentVerificationResult(false,null,"Payment is no longer payable.");

        var result=await _gateway.VerifyAsync(authority,payment.AmountIRR,ct);

        if(!result.IsSuccessful)
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                var current=await _payments.GetAsync(paymentId,token)??throw new DomainException("Payment not found.");
                if(current.Status is PaymentStatus.Pending or PaymentStatus.Redirected)
                {
                    current.Fail();
                    var transaction=await _payments.GetLatestTransactionAsync(paymentId,token);
                    transaction?.Fail();
                    await _uow.SaveChangesAsync(token);
                }
                return 0;
            },ct);

            return new PaymentVerificationResult(false,result.Reference,result.Error);
        }

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var current=await _payments.GetAsync(paymentId,token)??throw new DomainException("Payment not found.");
            if(current.Status==PaymentStatus.Succeeded) return 0;
            if(!string.Equals(current.Authority,authority,StringComparison.Ordinal))
                throw new DomainException("Payment authority does not match.");

            current.Succeed(result.Reference ?? authority);
            var transaction=await _payments.GetLatestTransactionAsync(paymentId,token);
            transaction?.Succeed(result.Reference ?? authority);
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        // Financial lifecycle runs in its own transaction and is idempotent for a succeeded payment.
        await _lifecycle.PaymentSucceededAsync(payment.OrderId,result.Reference ?? authority,ct);
        return new PaymentVerificationResult(true,result.Reference ?? authority,null);
    }
}
