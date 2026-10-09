using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class ProductReviewTests
{
    [Fact]
    public void NewReviewStartsPendingAndKeepsVerifiedOrderReference()
    {
        var review = ProductReview.Create(1, 2, 3, 4, 5, "عالی", "کیفیت محصول بسیار خوب بود و مطابق توضیحات رسید.");
        Assert.Equal(ProductReviewStatus.Pending, review.Status);
        Assert.Equal(4, review.OrderId);
        Assert.Equal(5, review.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RatingMustBeBetweenOneAndFive(int rating)
    {
        Assert.Throws<DomainException>(() => ProductReview.Create(1, 2, 3, 4, rating, "عنوان", "متن نظر معتبر و طولانی"));
    }

    [Fact]
    public void OnlyPendingReviewsCanBeModerated()
    {
        var review = ProductReview.Create(1, 2, 3, 4, 4, "عنوان", "متن نظر معتبر و طولانی");
        review.Approve(99);
        Assert.Equal(ProductReviewStatus.Approved, review.Status);
        Assert.Throws<DomainException>(() => review.Reject(99, "دلیل رد"));
    }

    [Fact]
    public void RejectionRequiresReason()
    {
        var review = ProductReview.Create(1, 2, 3, 4, 4, "عنوان", "متن نظر معتبر و طولانی");
        Assert.Throws<DomainException>(() => review.Reject(99, " "));
    }
}
