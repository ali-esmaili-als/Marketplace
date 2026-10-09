using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void Message_moves_from_pending_to_processing_to_processed()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var message = OutboxMessage.Create(1001, "Settlement.Completed", "{\"settlementId\":42}", now);

        Assert.True(message.IsClaimable(now));
        message.MarkProcessing(now, TimeSpan.FromMinutes(1));
        Assert.Equal("Processing", message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.False(message.IsClaimable(now));

        message.MarkProcessed(now.AddSeconds(2));

        Assert.Equal("Processed", message.Status);
        Assert.Equal(now.AddSeconds(2), message.ProcessedAtUtc);
        Assert.Null(message.LockedUntilUtc);
    }

    [Fact]
    public void Failed_delivery_is_retried_then_moved_to_dead_letter()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var message = OutboxMessage.Create(1002, "Settlement.Failed", "{\"settlementId\":43}", now);

        message.MarkProcessing(now, TimeSpan.FromMinutes(1));
        message.MarkFailed(now, "temporary transport error", maxAttempts: 3, TimeSpan.FromSeconds(10));

        Assert.Equal("Pending", message.Status);
        Assert.Equal(now.AddSeconds(10), message.NextAttemptAtUtc);
        Assert.False(message.IsClaimable(now));
        Assert.True(message.IsClaimable(now.AddSeconds(10)));

        message.MarkProcessing(now.AddSeconds(10), TimeSpan.FromMinutes(1));
        message.MarkFailed(now.AddSeconds(10), "temporary transport error", maxAttempts: 3, TimeSpan.FromSeconds(20));
        message.MarkProcessing(now.AddSeconds(30), TimeSpan.FromMinutes(1));
        message.MarkFailed(now.AddSeconds(30), "permanent transport error", maxAttempts: 3, TimeSpan.FromSeconds(40));

        Assert.Equal("DeadLetter", message.Status);
        Assert.Equal(3, message.Attempts);
        Assert.Equal("permanent transport error", message.LastError);
        Assert.False(message.IsClaimable(now.AddDays(1)));
    }

    [Fact]
    public void Processing_lease_can_be_reclaimed_after_worker_crash()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var message = OutboxMessage.Create(1003, "Settlement.Requested", "{\"settlementId\":44}", now);
        message.MarkProcessing(now, TimeSpan.FromMinutes(1));

        Assert.False(message.IsClaimable(now.AddSeconds(59)));
        Assert.True(message.IsClaimable(now.AddMinutes(1)));
    }

    [Fact]
    public void Lease_token_is_cleared_on_success_and_failure()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var success = OutboxMessage.Create(1004, "Settlement.Completed", "{}", now);
        var token = Guid.NewGuid();
        success.MarkProcessing(now, TimeSpan.FromMinutes(1), token);
        Assert.Equal(token, success.LockToken);
        success.MarkProcessed(now.AddSeconds(1));
        Assert.Null(success.LockToken);

        var failed = OutboxMessage.Create(1005, "Settlement.Failed", "{}", now);
        failed.MarkProcessing(now, TimeSpan.FromMinutes(1), token);
        failed.MarkFailed(now, "temporary", maxAttempts: 2, retryDelay: TimeSpan.FromSeconds(1));
        Assert.Null(failed.LockToken);
    }

    [Fact]
    public void Invalid_outbox_data_is_rejected()
    {
        Assert.Throws<DomainException>(() => OutboxMessage.Create(0, "Settlement.Completed", "{}"));
        Assert.Throws<DomainException>(() => OutboxMessage.Create(1, " ", "{}"));
        Assert.Throws<DomainException>(() => OutboxMessage.Create(1, "Settlement.Completed", " "));
    }
}
