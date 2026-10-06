using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Refunds.Ports;

public interface IRefundService
{
    Task<long> CreateAsync(
        long orderId,
        long paymentId,
        long amountIRR,
        string? reason,
        IReadOnlyList<RefundLineRequest> items,
        CancellationToken cancellationToken = default);
}

public sealed record RefundLineRequest(
    long OrderItemId,
    int Quantity,
    RefundInventoryDisposition InventoryDisposition);
