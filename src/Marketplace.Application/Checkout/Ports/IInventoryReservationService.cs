namespace Marketplace.Application.Checkout.Ports; public interface IInventoryReservationService{Task ReserveAsync(long orderId,IReadOnlyList<InventoryReservationRequest> items,CancellationToken cancellationToken=default);
    Task ConsumeAsync(long orderId,CancellationToken cancellationToken=default);
    Task ApplyRefundAsync(long orderId,IReadOnlyList<InventoryRefundRequest> items,CancellationToken cancellationToken=default);} public sealed record InventoryReservationRequest(long ProductId,long ProductVariantId,int Quantity);
public sealed record InventoryRefundRequest(long ProductVariantId,int Quantity,Marketplace.Domain.Refunds.RefundInventoryDisposition Disposition);
