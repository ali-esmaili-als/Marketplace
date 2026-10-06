using Marketplace.Domain.Common;

namespace Marketplace.Domain.Complaints;

public sealed class Complaint:AggregateRoot<long>
{
    private Complaint(){}
    public long OrderId{get;private set;} public long CustomerId{get;private set;} public long SellerId{get;private set;} public ComplaintStatus Status{get;private set;} public string Reason{get;private set;}=null!; public string? ResolutionNote{get;private set;} public DateTime CreatedAtUtc{get;private set;} public DateTime? ResolvedAtUtc{get;private set;}
    public static Complaint Create(long id,long orderId,long customerId,long sellerId,string reason){if(id<=0||orderId<=0||customerId<=0||sellerId<=0||string.IsNullOrWhiteSpace(reason))throw new DomainException("Invalid complaint.");return new Complaint{Id=id,OrderId=orderId,CustomerId=customerId,SellerId=sellerId,Reason=reason.Trim(),Status=ComplaintStatus.Open,CreatedAtUtc=DateTime.UtcNow};}
    public void StartReview(){if(Status!=ComplaintStatus.Open)throw new DomainException("Complaint is not open.");Status=ComplaintStatus.UnderReview;}
    public void ResolveForCustomer(string note){RequireReview(note);Status=ComplaintStatus.CustomerWon;ResolutionNote=note.Trim();ResolvedAtUtc=DateTime.UtcNow;}
    public void ResolveForSeller(string note){RequireReview(note);Status=ComplaintStatus.SellerWon;ResolutionNote=note.Trim();ResolvedAtUtc=DateTime.UtcNow;}
    public void Close(){if(Status is ComplaintStatus.Open or ComplaintStatus.UnderReview)throw new DomainException("Complaint must be resolved before closing.");Status=ComplaintStatus.Closed;}
    public void Cancel(){if(Status is ComplaintStatus.CustomerWon or ComplaintStatus.SellerWon or ComplaintStatus.Closed)throw new DomainException("Resolved complaint cannot be cancelled.");Status=ComplaintStatus.Cancelled;ResolvedAtUtc=DateTime.UtcNow;}
    private void RequireReview(string note){if(Status!=ComplaintStatus.UnderReview)throw new DomainException("Complaint is not under review.");if(string.IsNullOrWhiteSpace(note))throw new DomainException("Resolution note is required.");}
}