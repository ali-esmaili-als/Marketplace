using Marketplace.Application.Delivery.Ports;
using Marketplace.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController,Authorize]
[Route("api/orders/{orderId:long}/delivery")]
public sealed class DeliveryController(IDeliveryService delivery) : ControllerBase
{
    [RequirePermission(PermissionCodes.DeliveryConfirm)]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(long orderId, [FromBody] ConfirmDeliveryRequest request, CancellationToken cancellationToken)
    {
        await delivery.ConfirmAsync(orderId, request.Code, cancellationToken);
        return NoContent();
    }

    public sealed record ConfirmDeliveryRequest(string Code);
}