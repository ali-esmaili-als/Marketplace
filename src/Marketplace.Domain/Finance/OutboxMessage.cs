using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

/// <summary>
/// Durable application event persisted in the same SQL transaction as the business change.
/// Delivery is at-least-once; consumers must deduplicate by MessageId.
/// </summary>
public sealed class OutboxMessage : Entity<long>
{
    private OutboxMessage() { }

    public Guid MessageId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public Guid? LockToken { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? LastError { get; private set; }

    public static OutboxMessage Create(long id, string eventType, string payloadJson, DateTime? nowUtc = null)
    {
        if (id <= 0) throw new DomainException("Outbox ID must be positive.");
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 200)
            throw new DomainException("Outbox event type is invalid.");
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > 1000000)
            throw new DomainException("Outbox payload is invalid.");
        var now = nowUtc ?? DateTime.UtcNow;
        return new OutboxMessage
        {
            Id = id,
            MessageId = Guid.NewGuid(),
            EventType = eventType.Trim(),
            PayloadJson = payloadJson,
            OccurredAtUtc = now,
            NextAttemptAtUtc = now,
            Status = "Pending"
        };
    }

    public bool IsClaimable(DateTime nowUtc)
        => (Status == "Pending" || Status == "Processing" && LockedUntilUtc <= nowUtc)
           && NextAttemptAtUtc <= nowUtc;

    public void MarkProcessing(DateTime nowUtc, TimeSpan lease, Guid? lockToken = null)
    {
        if (!IsClaimable(nowUtc)) throw new DomainException("Outbox message is not claimable.");
        Status = "Processing";
        Attempts++;
        LockedUntilUtc = nowUtc.Add(lease);
        LockToken = lockToken;
        LastError = null;
    }

    public void MarkProcessed(DateTime nowUtc)
    {
        if (Status != "Processing") throw new DomainException("Only a processing outbox message can be completed.");
        Status = "Processed";
        ProcessedAtUtc = nowUtc;
        LockedUntilUtc = null;
        LockToken = null;
        LastError = null;
    }

    public void RetryFromDeadLetter(DateTime nowUtc)
    {
        if (Status != "DeadLetter")
            throw new DomainException("Only a dead-letter outbox message can be retried manually.");

        Status = "Pending";
        Attempts = 0;
        NextAttemptAtUtc = nowUtc;
        LockedUntilUtc = null;
        LockToken = null;
        ProcessedAtUtc = null;
    }

    public void MarkFailed(DateTime nowUtc, string error, int maxAttempts, TimeSpan retryDelay)
    {
        if (Status != "Processing") throw new DomainException("Only a processing outbox message can fail.");
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown delivery error." : error[..Math.Min(error.Length, 2000)];
        LockedUntilUtc = null;
        LockToken = null;
        if (Attempts >= maxAttempts)
        {
            Status = "DeadLetter";
            return;
        }

        Status = "Pending";
        NextAttemptAtUtc = nowUtc.Add(retryDelay);
    }
}
