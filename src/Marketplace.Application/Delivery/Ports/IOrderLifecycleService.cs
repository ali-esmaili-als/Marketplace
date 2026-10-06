namespace Marketplace.Application.Delivery.Ports;

public interface IOrderLifecycleService
{
    Task StartPreparingAsync(long orderId, CancellationToken cancellationToken = default);
    Task<DeliveryCodeResult> MarkReadyAsync(long orderId, CancellationToken cancellationToken = default);
    Task ConfirmAsync(long orderId, string code, CancellationToken cancellationToken = default);
    Task ExpireAsync(long orderId, CancellationToken cancellationToken = default);
    Task CompleteAsync(long orderId, CancellationToken cancellationToken = default);
}

public sealed record DeliveryCodeResult(long OrderId, string Code, DateTime ExpiresAtUtc);