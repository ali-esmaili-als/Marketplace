namespace Marketplace.Application.Refunds.Ports;

public interface IRefundService
{
    Task<long> CreateAsync(long orderId, long paymentId, long amountIRR, string? reason, CancellationToken cancellationToken = default);
}