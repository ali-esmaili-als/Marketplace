using Marketplace.Application.Refunds.Ports;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/refunds")]
public sealed class RefundController(IRefundService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<long>> Create([FromBody] CreateRefundRequest request, CancellationToken cancellationToken)
        => Ok(await service.CreateAsync(request.OrderId, request.PaymentId, request.AmountIRR, request.Reason, cancellationToken));

    public sealed record CreateRefundRequest(long OrderId, long PaymentId, long AmountIRR, string? Reason);
}