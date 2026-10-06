using Marketplace.Application.Payments.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentController(IPaymentCompletionService completion) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("{paymentAttemptId:long}/complete")]
    public async Task<IActionResult> Complete(
        long paymentAttemptId,
        [FromBody] CompletePaymentRequest request,
        CancellationToken cancellationToken)
    {
        await completion.CompleteAsync(paymentAttemptId, request.GatewayTransactionId, cancellationToken);
        return NoContent();
    }

    public sealed record CompletePaymentRequest(string GatewayTransactionId);
}