using Marketplace.Application.Checkout.Commands;
using Marketplace.Application.Checkout.Results;
using Marketplace.Application.Checkout.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController,Authorize]
[Route("api/checkout")]
public sealed class CheckoutController(CheckoutPayService checkout) : ControllerBase
{
    [HttpPost("pay")]
    public async Task<ActionResult<CheckoutPayResult>> Pay(
        [FromBody] CheckoutPayCommand command,
        CancellationToken cancellationToken)
        => Ok(await checkout.ExecuteAsync(command, cancellationToken));
}