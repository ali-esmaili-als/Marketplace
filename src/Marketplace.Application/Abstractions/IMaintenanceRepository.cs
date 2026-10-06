using Marketplace.Domain.Delivery;
using Marketplace.Domain.Inventory;
namespace Marketplace.Application.Abstractions;
public interface IMaintenanceRepository
{
 Task<List<Delivery>> GetExpiredDeliveriesAsync(DateTime nowUtc,CancellationToken ct=default);
 Task<List<InventoryReservation>> GetExpiredReservationsAsync(DateTime nowUtc,CancellationToken ct=default);
 Task<List<Marketplace.Domain.Orders.Order>> GetOrdersReadyToCompleteAsync(DateTime nowUtc,CancellationToken ct=default);
 Task<InventoryItem?> GetInventoryItemAsync(long variantId,CancellationToken ct=default);
 Task<bool> HasOpenComplaintAsync(long orderId,CancellationToken ct=default);
}