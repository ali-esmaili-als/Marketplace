namespace Marketplace.Application.Delivery.Ports;

public interface IDeliveryService
{
    Task ConfirmAsync(long orderId, string code, CancellationToken cancellationToken = default);
}