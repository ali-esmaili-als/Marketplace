using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public sealed class Payment : AggregateRoot<long>
{
    private Payment(){}
    public long OrderId {get;private set;}
    public long CustomerId {get;private set;}
    public long AmountIRR {get;private set;}
    public PaymentStatus Status {get;private set;}
    public string? Provider {get;private set;}
    public string? Authority {get;private set;}
    public string? ReferenceNumber {get;private set;}
    public DateTime CreatedAtUtc {get;private set;}
    public DateTime? PaidAtUtc {get;private set;}
    public DateTime? RefundedAtUtc {get;private set;}
    public static Payment Create(long id,long orderId,long customerId,long amountIrr){if(id<=0||orderId<=0||customerId<=0||amountIrr<=0)throw new DomainException("Invalid payment.");return new Payment{Id=id,OrderId=orderId,CustomerId=customerId,AmountIRR=amountIrr,Status=PaymentStatus.Pending,CreatedAtUtc=DateTime.UtcNow};}
    public void Redirect(string provider,string authority){if(Status!=PaymentStatus.Pending)throw new DomainException("Payment cannot be redirected.");if(string.IsNullOrWhiteSpace(provider)||string.IsNullOrWhiteSpace(authority))throw new DomainException("Provider and authority are required.");Provider=provider.Trim();Authority=authority.Trim();Status=PaymentStatus.Redirected;}
    public void Succeed(string reference){if(Status is not (PaymentStatus.Pending or PaymentStatus.Redirected))throw new DomainException("Payment cannot succeed.");Status=PaymentStatus.Succeeded;ReferenceNumber=reference?.Trim();PaidAtUtc=DateTime.UtcNow;}
    public void Fail(){if(Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)throw new DomainException("Payment cannot fail.");Status=PaymentStatus.Failed;}
    public void Cancel(){if(Status==PaymentStatus.Succeeded)throw new DomainException("Succeeded payment cannot be cancelled.");Status=PaymentStatus.Cancelled;}
    public void RequireReconciliation(string reference)
    {
        if(Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            throw new DomainException("Refunded payment cannot require reconciliation.");
        if(string.IsNullOrWhiteSpace(reference))
            throw new DomainException("Gateway reference is required for reconciliation.");
        Status=PaymentStatus.ReconciliationRequired;
        ReferenceNumber=reference.Trim();
    }
    public void MarkRefunded(){if(Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded or PaymentStatus.ReconciliationRequired))throw new DomainException("Payment cannot be refunded.");Status=PaymentStatus.Refunded;RefundedAtUtc=DateTime.UtcNow;}
}