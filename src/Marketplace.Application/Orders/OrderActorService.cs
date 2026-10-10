using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Orders;

public sealed class OrderActorService
{
    // This is a platform policy, not a value that the seller/browser may choose.
    // Keep the duration centralized so it can later be moved to validated configuration.
    private static readonly TimeSpan ComplaintWindow = TimeSpan.FromDays(7);

    private readonly ISellerManagementRepository _sellers;
    private readonly IOrderRepository _orders;
    private readonly OrderLifecycleService _lifecycle;
    private readonly RefundService _refunds;

    public OrderActorService(
        ISellerManagementRepository sellers,
        IOrderRepository orders,
        OrderLifecycleService lifecycle,
        RefundService refunds)
    {
        _sellers = sellers;
        _orders = orders;
        _lifecycle = lifecycle;
        _refunds = refunds;
    }

    private async Task<long> SellerId(long userId, CancellationToken ct) =>
        (await _sellers.GetSellerByUserIdAsync(userId, ct)
            ?? throw new DomainException("Seller profile not found.")).Id;

    private async Task<Order> Own(long orderId, long userId, bool seller, CancellationToken ct)
    {
        var order = await _orders.GetAsync(orderId, ct)
            ?? throw new DomainException("Order not found.");
        var sellerId = seller ? await SellerId(userId, ct) : 0;
        if (seller ? order.SellerId != sellerId : order.CustomerId != userId)
            throw new DomainException("You do not own this order.");
        return order;
    }

    public async Task ReadyAsync(long userId, long orderId, CancellationToken ct = default)
    {
        await Own(orderId, userId, true, ct);
        await _lifecycle.MarkReadyForDeliveryAsync(orderId, ct);
    }

    public async Task DeliverAsync(
        long userId,
        long orderId,
        string code,
        string reference,
        DateTime deliveredAt,
        DateTime complaintExpires,
        CancellationToken ct = default)
    {
        await Own(orderId, userId, true, ct);

        // Both timestamps originate in the request and are therefore untrusted.
        // Use one server UTC instant for code expiry, delivery state, and the complaint
        // deadline. A client must not backdate delivery or extend/reduce the complaint
        // window by changing request fields. Parameters remain for API compatibility.
        var deliveredAtUtc = DateTime.UtcNow;
        var complaintExpiresAtUtc = deliveredAtUtc.Add(ComplaintWindow);

        await _lifecycle.MarkDeliveredAsync(
            orderId,
            code,
            reference,
            deliveredAtUtc,
            complaintExpiresAtUtc,
            ct);
    }

    public async Task ExpireAsync(long userId, long orderId, CancellationToken ct = default)
    {
        await Own(orderId, userId, true, ct);
        await _lifecycle.ExpireDeliveryAsync(orderId, DateTime.UtcNow, ct);
    }

    public async Task<long> ComplaintAsync(long userId, long orderId, string reason, CancellationToken ct = default)
    {
        await Own(orderId, userId, false, ct);
        return await _lifecycle.OpenComplaintAsync(orderId, userId, reason, ct);
    }

    public async Task RefundAsync(long userId, long orderId, RefundReason reason, CancellationToken ct = default)
    {
        await Own(orderId, userId, false, ct);
        await _refunds.ProcessAsync(orderId, reason, ct);
    }
}
