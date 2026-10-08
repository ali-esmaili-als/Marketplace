using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Orders;

public sealed record PaymentVerificationResult(bool Paid, string? Reference, string? Error);

public sealed class PaymentVerificationService
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentGatewayFactory _gatewayFactory;
    private readonly IUnitOfWork _uow;
    private readonly OrderLifecycleService _lifecycle;

    public PaymentVerificationService(IPaymentRepository payments,IPaymentGatewayFactory gatewayFactory,IUnitOfWork uow,OrderLifecycleService lifecycle)
    {
        _payments=payments; _gatewayFactory=gatewayFactory; _uow=uow; _lifecycle=lifecycle;
    }

    public async Task<PaymentVerificationResult> VerifyTestReturnAsync(long paymentId,string authority,bool success,CancellationToken ct=default)
    {
        if (paymentId <= 0 || string.IsNullOrWhiteSpace(authority))
            throw new DomainException("Test payment return is invalid.");

        var payment = await _payments.GetAsync(paymentId, ct) ?? throw new DomainException("Payment not found.");
        if (!string.Equals(payment.Provider, "TEST_BANK", StringComparison.Ordinal)
            || !string.Equals(payment.Authority, authority, StringComparison.Ordinal))
            throw new DomainException("Test return does not match this payment.");

        if (payment.Status == PaymentStatus.Succeeded)
            return new PaymentVerificationResult(true, payment.ReferenceNumber, null);
        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
            return new PaymentVerificationResult(false, null, "Payment is no longer payable.");

        if (!success)
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                var current = await _payments.GetAsync(paymentId, token) ?? throw new DomainException("Payment not found.");
                if (current.Status is PaymentStatus.Pending or PaymentStatus.Redirected)
                {
                    current.Fail();
                    var transaction = await _payments.GetLatestTransactionAsync(paymentId, token);
                    transaction?.Fail();
                    await _uow.SaveChangesAsync(token);
                }
                return 0;
            }, ct);
            return new PaymentVerificationResult(false, null, "Test payment was cancelled.");
        }

        var gateway = await _gatewayFactory.GetForExistingPaymentAsync(PaymentProviderCode.TestBank, ct);
        var verification = await gateway.VerifyAsync(authority, payment.AmountIRR, ct);
        if (!verification.IsSuccessful)
            return new PaymentVerificationResult(false, null, verification.Error);

        await _lifecycle.PaymentSucceededAsync(payment.OrderId, verification.Reference ?? authority, ct);
        return new PaymentVerificationResult(true, verification.Reference ?? authority, null);
    }

    public async Task<PaymentVerificationResult> VerifyAsync(long userId,long paymentId,string authority,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(authority)) throw new DomainException("Payment authority is required.");

        var payment=await _payments.GetAsync(paymentId,ct)??throw new DomainException("Payment not found.");
        if(payment.CustomerId!=userId) throw new DomainException("Customer does not own this payment.");
        if(!string.Equals(payment.Authority,authority,StringComparison.Ordinal))
            throw new DomainException("Payment authority does not match.");

        if(payment.Status==PaymentStatus.Succeeded)
            return new PaymentVerificationResult(true,payment.ReferenceNumber,null);

        if(payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
            return new PaymentVerificationResult(false,null,"Payment is no longer payable.");

        var provider=Enum.TryParse<PaymentProviderCode>(payment.Provider,true,out var parsed) ? parsed : throw new DomainException("Invalid payment provider.");
        var gateway=await _gatewayFactory.GetAsync(provider,ct);
        var result=await gateway.VerifyAsync(authority,payment.AmountIRR,ct);

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

        // Payment status and seller financial movement are finalized together by the lifecycle service.
        await _lifecycle.PaymentSucceededAsync(payment.OrderId,result.Reference ?? authority,ct);

        return new PaymentVerificationResult(true,result.Reference ?? authority,null);
    }
}
