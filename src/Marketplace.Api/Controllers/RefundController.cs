using Marketplace.Infrastructure.Authorization;
using Marketplace.Application.Authorization;
using Marketplace.Application.Refunds.Ports;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Refunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController, Authorize]
[Route("api/refunds")]
public sealed class RefundController(IRefundService service) : ControllerBase
{
    [ActionAccess(UserTypeId.Admin, UserTypeId.Customer)]
    [HttpPost]
    public async Task<ActionResult<long>> Create(
        CreateRefundRequest request,
        CancellationToken ct)
        => Ok(await service.CreateAsync(
            request.OrderId,
            request.PaymentId,
            request.AmountIRR,
            request.Reason,
            request.Items.Select(x => new RefundLineRequest(
                x.OrderItemId,
                x.Quantity,
                x.InventoryDisposition)).ToArray(),
            ct));

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{refundId:long}/complete")]
    public async Task<IActionResult> Complete(
        long refundId,
        CompleteRefundRequest request,
        CancellationToken ct)
    {
        await service.CompleteAsync(refundId, request.GatewayRefundReference, ct);
        return NoContent();
    }

    public sealed record CompleteRefundRequest(string GatewayRefundReference);

    public sealed record CreateRefundRequest(
        long OrderId,
        long PaymentId,
        long AmountIRR,
        string? Reason,
        IReadOnlyList<RefundLineRequestDto> Items);

    public sealed record RefundLineRequestDto(
        long OrderItemId,
        int Quantity,
        RefundInventoryDisposition InventoryDisposition);
}
