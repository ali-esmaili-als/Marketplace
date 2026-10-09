using Marketplace.Domain.Notifications;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class NotificationTests
{
    [Fact]
    public void In_app_notification_moves_from_pending_to_sent_to_read()
    {
        var notification = Notification.Create(
            91001,
            4201,
            NotificationChannel.InApp,
            "Order paid",
            "Payment was confirmed",
            "Order",
            7201);

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Null(notification.ReadAtUtc);

        notification.MarkSent();

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAtUtc);
        Assert.Null(notification.ReadAtUtc);

        notification.MarkRead();

        Assert.Equal(NotificationStatus.Read, notification.Status);
        Assert.NotNull(notification.ReadAtUtc);
    }

    [Fact]
    public void Marking_an_already_read_notification_is_idempotent()
    {
        var notification = Notification.Create(91002, 4201, NotificationChannel.InApp, "Title", "Body");
        notification.MarkSent();
        notification.MarkRead();
        var readAt = notification.ReadAtUtc;

        notification.MarkRead();

        Assert.Equal(NotificationStatus.Read, notification.Status);
        Assert.Equal(readAt, notification.ReadAtUtc);
    }

    [Fact]
    public void Invalid_notification_identity_or_content_is_rejected()
    {
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            Notification.Create(0, 1, NotificationChannel.InApp, "Title", "Body"));
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            Notification.Create(1, 0, NotificationChannel.InApp, "Title", "Body"));
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            Notification.Create(1, 1, NotificationChannel.InApp, " ", "Body"));
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            Notification.Create(1, 1, NotificationChannel.InApp, "Title", " "));
    }
}
