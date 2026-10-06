using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class CommissionReversal : Entity<long>
{
    private CommissionReversal() { }

    public long CommissionId { get; private set; }
    public long OrderId { get; private set; }
    public long RefundId { get; private set; }
    public long RefundAmountIRR { get; private set; }
    public long ReversedCommissionIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static CommissionReversal Create(long id, long commissionId, long orderId, long refundId,
        long refundAmount, long reversed)
    {
        if (refundAmount <= 0 || reversed < 0 || reversed > refundAmount)
            throw new DomainException("Invalid reversal.");

        return new()
        {
            Id = id, CommissionId = commissionId, OrderId = orderId, RefundId = refundId,
            RefundAmountIRR = refundAmount, ReversedCommissionIRR = reversed,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}