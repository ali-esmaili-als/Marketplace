namespace Marketplace.Application.Abstractions;

/// <summary>
/// Publishes an outbox event to a configured downstream transport.
/// Implementations must be idempotent by MessageId or accept at-least-once delivery.
/// </summary>
public interface IOutboxPublisher
{
    Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default);
}

public sealed record OutboxEnvelope(
    Guid MessageId,
    string EventType,
    string PayloadJson,
    DateTime OccurredAtUtc);
