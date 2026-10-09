using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public enum ProductReviewStatus : byte
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public sealed class ProductReview : AggregateRoot<long>
{
    private ProductReview() { }

    public long ProductId { get; private set; }
    public long CustomerId { get; private set; }
    public long OrderId { get; private set; }
    public byte Rating { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public ProductReviewStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ModeratedAtUtc { get; private set; }
    public long? ModeratorUserId { get; private set; }
    public string? ModerationNote { get; private set; }

    public static ProductReview Create(long id, long productId, long customerId, long orderId, int rating, string title, string body)
    {
        if (id <= 0 || productId <= 0 || customerId <= 0 || orderId <= 0)
            throw new DomainException("Review identifiers must be positive.");
        if (rating is < 1 or > 5) throw new DomainException("Rating must be between 1 and 5.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 150)
            throw new DomainException("Review title is required and must not exceed 150 characters.");
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length < 10 || body.Trim().Length > 3000)
            throw new DomainException("Review text must contain 10 to 3000 characters.");

        return new ProductReview
        {
            Id = id, ProductId = productId, CustomerId = customerId, OrderId = orderId,
            Rating = (byte)rating, Title = title.Trim(), Body = body.Trim(),
            Status = ProductReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Approve(long moderatorUserId)
    {
        if (moderatorUserId <= 0) throw new DomainException("Moderator identifier must be positive.");
        if (Status != ProductReviewStatus.Pending) throw new DomainException("Only pending reviews can be moderated.");
        Status = ProductReviewStatus.Approved;
        ModeratorUserId = moderatorUserId;
        ModeratedAtUtc = DateTime.UtcNow;
        ModerationNote = null;
    }

    public void Reject(long moderatorUserId, string note)
    {
        if (moderatorUserId <= 0) throw new DomainException("Moderator identifier must be positive.");
        if (Status != ProductReviewStatus.Pending) throw new DomainException("Only pending reviews can be moderated.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 1000)
            throw new DomainException("A rejection note of at most 1000 characters is required.");
        Status = ProductReviewStatus.Rejected;
        ModeratorUserId = moderatorUserId;
        ModeratedAtUtc = DateTime.UtcNow;
        ModerationNote = note.Trim();
    }
}
