using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class ComplaintTests
{
    [Fact]
    public void NewComplaintCanBeCancelledBeforeResolution()
    {
        var complaint = Complaint.Create(1, 2, 3, 4, "سفارش با کالای آسیب‌دیده تحویل شد");
        complaint.Cancel();
        Assert.Equal(ComplaintStatus.Cancelled, complaint.Status);
        Assert.NotNull(complaint.ResolvedAtUtc);
    }

    [Fact]
    public void ResolvedComplaintCannotBeCancelled()
    {
        var complaint = Complaint.Create(1, 2, 3, 4, "سفارش با کالای آسیب‌دیده تحویل شد");
        complaint.StartReview();
        complaint.ResolveForCustomer("مدارک بررسی شد و شکایت تأیید شد.");
        Assert.Throws<DomainException>(() => complaint.Cancel());
    }

    [Fact]
    public void OpenComplaintMustEnterReviewBeforeResolution()
    {
        var complaint = Complaint.Create(1, 2, 3, 4, "سفارش با کالای آسیب‌دیده تحویل شد");
        Assert.Throws<DomainException>(() => complaint.ResolveForSeller("بررسی انجام شد."));
    }
}