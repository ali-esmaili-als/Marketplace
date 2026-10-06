using Marketplace.Application.Payments.Ports;
using Marketplace.Infrastructure.Authorization;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentController(IPaymentCompletionService completion, IPaymentLifecycleService lifecycle) : ControllerBase
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

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{paymentAttemptId:long}/fail")]
    public async Task<IActionResult> Fail(long paymentAttemptId, CancellationToken cancellationToken)
    {
        await lifecycle.FailAsync(paymentAttemptId, cancellationToken);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{paymentAttemptId:long}/cancel")]
    public async Task<IActionResult> Cancel(long paymentAttemptId, CancellationToken cancellationToken)
    {
        await lifecycle.CancelAsync(paymentAttemptId, cancellationToken);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{paymentAttemptId:long}/expire")]
    public async Task<IActionResult> Expire(long paymentAttemptId, CancellationToken cancellationToken)
    {
        await lifecycle.ExpireAsync(paymentAttemptId, cancellationToken);
        return NoContent();
    }

    public sealed record CompletePaymentRequest(string GatewayTransactionId);
}