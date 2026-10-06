using Marketplace.Infrastructure.Authorization;
using Marketplace.Application.Delivery.Ports;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController, Authorize]
[Route("api/orders/{orderId:long}/delivery")]
public sealed class DeliveryController(IOrderLifecycleService lifecycle) : ControllerBase
{
    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller)]
    [HttpPost("preparing")]
    public async Task<IActionResult> Preparing(long orderId, CancellationToken cancellationToken)
    {
        await lifecycle.StartPreparingAsync(orderId, cancellationToken);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller)]
    [HttpPost("ready")]
    public async Task<ActionResult<DeliveryCodeResult>> Ready(long orderId, CancellationToken cancellationToken)
        => Ok(await lifecycle.MarkReadyAsync(orderId, cancellationToken));

    [ActionAccess(UserTypeId.Admin, UserTypeId.Customer)]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        long orderId,
        [FromBody] ConfirmDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        await lifecycle.ConfirmAsync(orderId, request.Code, cancellationToken);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller)]
    [HttpPost("expire")]
    public async Task<IActionResult> Expire(long orderId, CancellationToken cancellationToken)
    {
        await lifecycle.ExpireAsync(orderId, cancellationToken);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin, UserTypeId.Customer)]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(long orderId, CancellationToken cancellationToken)
    {
        await lifecycle.CompleteAsync(orderId, cancellationToken);
        return NoContent();
    }

    public sealed record ConfirmDeliveryRequest(string Code);
}