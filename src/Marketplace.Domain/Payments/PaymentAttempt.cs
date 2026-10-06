using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public sealed class PaymentAttempt : AggregateRoot<long>
{
    private PaymentAttempt() { }
    public long OrderId { get; private set; }
    public long CustomerId { get; private set; }
    public long AmountIRR { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal FxRateToIRR { get; private set; }
    public string Gateway { get; private set; } = null!;
    public string? GatewayTransactionId { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static PaymentAttempt Create(long id,long orderId,long customerId,long amountIrr,string currencyCode,decimal fxRateToIrr,string gateway)
    {
        if(amountIrr<=0) throw new DomainException("Payment amount must be positive.");
        if(string.IsNullOrWhiteSpace(currencyCode) || fxRateToIrr<=0 || string.IsNullOrWhiteSpace(gateway)) throw new DomainException("Invalid payment snapshot.");
        return new PaymentAttempt { Id=id, OrderId=orderId, CustomerId=customerId, AmountIRR=amountIrr, CurrencyCode=currencyCode.Trim().ToUpperInvariant(), FxRateToIRR=fxRateToIrr, Gateway=gateway.Trim(), Status=PaymentAttemptStatus.Pending, CreatedAtUtc=DateTime.UtcNow };
    }
    public void MarkRedirected(){RequireOpen();Status=PaymentAttemptStatus.Redirected;}
    public void MarkSucceeded(string? gatewayTransactionId){RequireOpen();Status=PaymentAttemptStatus.Succeeded;GatewayTransactionId=gatewayTransactionId;CompletedAtUtc=DateTime.UtcNow;}
    public void MarkFailed(){RequireOpen();Status=PaymentAttemptStatus.Failed;CompletedAtUtc=DateTime.UtcNow;}
    public void Cancel(){RequireOpen();Status=PaymentAttemptStatus.Cancelled;CompletedAtUtc=DateTime.UtcNow;}
    public void Expire(){RequireOpen();Status=PaymentAttemptStatus.Expired;CompletedAtUtc=DateTime.UtcNow;}
    private void RequireOpen(){if(Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Failed or PaymentAttemptStatus.Cancelled or PaymentAttemptStatus.Expired)throw new DomainException("Payment attempt is terminal.");}
}
