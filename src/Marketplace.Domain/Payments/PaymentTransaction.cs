using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public enum PaymentTransactionStatus:byte { Initiated=1,Succeeded=2,Failed=3,Reversed=4 }
public sealed class PaymentTransaction:Entity<long>
{
    private PaymentTransaction(){}
    public long PaymentId{get;private set;} public long AmountIRR{get;private set;} public PaymentTransactionStatus Status{get;private set;}
    public string Provider{get;private set;}=null!; public string? Authority{get;private set;} public string? Reference{get;private set;} public DateTime CreatedAtUtc{get;private set;}
    public static PaymentTransaction Create(long id,long paymentId,long amountIrr,string provider,string? authority=null){if(id<=0||paymentId<=0||amountIrr<=0||string.IsNullOrWhiteSpace(provider))throw new DomainException("Invalid payment transaction.");return new PaymentTransaction{Id=id,PaymentId=paymentId,AmountIRR=amountIrr,Provider=provider.Trim(),Authority=authority?.Trim(),Status=PaymentTransactionStatus.Initiated,CreatedAtUtc=DateTime.UtcNow};}
    public void Succeed(string reference)
    {
        if(Status!=PaymentTransactionStatus.Initiated)
            throw new DomainException("Transaction is not initiated.");
        if(string.IsNullOrWhiteSpace(reference))
            throw new DomainException("A bank reference is required for a successful payment transaction.");

        Status=PaymentTransactionStatus.Succeeded;
        Reference=reference.Trim();
    }
    public void Fail(){if(Status!=PaymentTransactionStatus.Initiated)throw new DomainException("Transaction is not initiated.");Status=PaymentTransactionStatus.Failed;}
    public void Reverse(){if(Status!=PaymentTransactionStatus.Succeeded)throw new DomainException("Only successful transaction can be reversed.");Status=PaymentTransactionStatus.Reversed;}
}