using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public enum RefundInventoryDisposition : byte
{
    ReturnToStock = 1,
    Damaged = 2,
    Inspection = 3,
    NotReturnable = 4
}

public sealed class RefundItem : Entity<long>
{
    private RefundItem() { }

    public long RefundId { get; private set; }
    public long OrderItemId { get; private set; }
    public int Quantity { get; private set; }
    public long AmountIRR { get; private set; }
    public RefundInventoryDisposition InventoryDisposition { get; private set; }

    public static RefundItem Create(long id, long refundId, long orderItemId, int quantity, long amount,
        RefundInventoryDisposition disposition)
    {
        if (quantity <= 0 || amount <= 0) throw new DomainException("Invalid refund item.");
        return new()
        {
            Id = id, RefundId = refundId, OrderItemId = orderItemId,
            Quantity = quantity, AmountIRR = amount, InventoryDisposition = disposition
        };
    }
}