using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public enum RefundReason:byte { DeliveryExpired=1,ComplaintCustomerWon=2,AdminAdjustment=3,Other=4 }
public sealed class Refund:AggregateRoot<long>
{
    private Refund(){}
    public long OrderId{get;private set;} public long PaymentId{get;private set;} public long CustomerId{get;private set;} public long AmountIRR{get;private set;} public RefundReason Reason{get;private set;} public RefundStatus Status{get;private set;} public string? ProviderReference{get;private set;} public string? FailureReason{get;private set;} public DateTime RequestedAtUtc{get;private set;} public DateTime? CompletedAtUtc{get;private set;}
    public static Refund Create(long id,long orderId,long paymentId,long customerId,long amountIrr,RefundReason reason){if(id<=0||orderId<=0||paymentId<=0||customerId<=0||amountIrr<=0)throw new DomainException("Invalid refund.");return new Refund{Id=id,OrderId=orderId,PaymentId=paymentId,CustomerId=customerId,AmountIRR=amountIrr,Reason=reason,Status=RefundStatus.Requested,RequestedAtUtc=DateTime.UtcNow};}
    public void Approve(){Require(RefundStatus.Requested);Status=RefundStatus.Approved;}
    public void StartProcessing(){if(Status is not (RefundStatus.Requested or RefundStatus.Approved))throw new DomainException("Refund cannot be processed.");Status=RefundStatus.Processing;}
    public void Complete(string? providerReference){if(Status is not (RefundStatus.Processing or RefundStatus.Approved))throw new DomainException("Refund cannot complete.");Status=RefundStatus.Completed;ProviderReference=providerReference?.Trim();CompletedAtUtc=DateTime.UtcNow;}
    public void Fail(string reason){if(Status is RefundStatus.Completed or RefundStatus.Rejected)throw new DomainException("Refund cannot fail.");if(string.IsNullOrWhiteSpace(reason))throw new DomainException("Failure reason is required.");Status=RefundStatus.Failed;FailureReason=reason.Trim();CompletedAtUtc=DateTime.UtcNow;}
    public void Reject(string reason){if(Status is RefundStatus.Completed)throw new DomainException("Completed refund cannot be rejected.");if(string.IsNullOrWhiteSpace(reason))throw new DomainException("Rejection reason is required.");Status=RefundStatus.Rejected;FailureReason=reason.Trim();CompletedAtUtc=DateTime.UtcNow;}
    private void Require(RefundStatus s){if(Status!=s)throw new DomainException($"Refund must be in {s} status.");}
}