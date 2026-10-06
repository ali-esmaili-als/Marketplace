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
