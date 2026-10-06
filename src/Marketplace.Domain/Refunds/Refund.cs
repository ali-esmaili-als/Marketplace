using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public sealed class Refund : AggregateRoot<long>
{
    private readonly List<RefundItem> _items = [];
    private Refund() { }

    public long OrderId { get; private set; }
    public long PaymentId { get; private set; }
    public long AmountIRR { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<RefundItem> Items => _items.AsReadOnly();

    public static Refund Create(long id, long orderId, long paymentId, long amount, string? reason)
    {
        if (amount <= 0) throw new DomainException("Refund amount must be positive.");
        return new()
        {
            Id = id, OrderId = orderId, PaymentId = paymentId, AmountIRR = amount,
            Status = RefundStatus.Requested, Reason = reason?.Trim(), CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void AddItem(RefundItem item)
    {
        if (Status != RefundStatus.Requested) throw new DomainException("Refund is no longer editable.");
        _items.Add(item);
    }

    public void StartProcessing()
    {
        if (Status != RefundStatus.Requested) throw new DomainException("Invalid refund state.");
        Status = RefundStatus.Processing;
    }

    public void Complete()
    {
        if (Status != RefundStatus.Processing) throw new DomainException("Invalid refund state.");
        Status = RefundStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string? reason = null)
    {
        if (Status is RefundStatus.Completed or RefundStatus.Cancelled) throw new DomainException("Refund is terminal.");
        Status = RefundStatus.Rejected;
        Reason = reason?.Trim() ?? Reason;
    }

    public void Cancel()
    {
        if (Status is RefundStatus.Completed) throw new DomainException("Refund is completed.");
        Status = RefundStatus.Cancelled;
    }
}