using Marketplace.Domain.Common;

namespace Marketplace.Domain.Complaints;

public sealed class Complaint : AggregateRoot<long>
{
    private Complaint() { }

    public long OrderId { get; private set; }
    public long CustomerId { get; private set; }
    public long SellerId { get; private set; }
    public ComplaintStatus Status { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    public static Complaint Create(long id, long orderId, long customerId, long sellerId, string subject, string description)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
            throw new DomainException("Complaint subject and description are required.");

        var now = DateTime.UtcNow;
        return new()
        {
            Id = id, OrderId = orderId, CustomerId = customerId, SellerId = sellerId,
            Status = ComplaintStatus.Open, Subject = subject.Trim(), Description = description.Trim(),
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
    }

    public void StartReview() { RequireOpen(); Status = ComplaintStatus.UnderReview; Touch(); }
    public void ResolveForCustomer() { RequireReview(); Status = ComplaintStatus.ResolvedForCustomer; Touch(); }
    public void ResolveForSeller() { RequireReview(); Status = ComplaintStatus.ResolvedForSeller; Touch(); }
    public void Close()
    {
        if (Status is not (ComplaintStatus.ResolvedForCustomer or ComplaintStatus.ResolvedForSeller))
            throw new DomainException("Complaint is not resolved.");
        Status = ComplaintStatus.Closed; ClosedAtUtc = DateTime.UtcNow; Touch();
    }

    private void RequireOpen() { if (Status != ComplaintStatus.Open) throw new DomainException("Complaint is not open."); }
    private void RequireReview() { if (Status != ComplaintStatus.UnderReview) throw new DomainException("Complaint is not under review."); }
    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}