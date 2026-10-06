using Marketplace.Application.Finance.Ports;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/settlements")]
public sealed class SettlementController(ISettlementService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<long>> Request([FromBody] RequestSettlementRequest request, CancellationToken cancellationToken)
        => Ok(await service.RequestAsync(request.SellerId, request.BankAccountId, request.AmountIRR, cancellationToken));

    public sealed record RequestSettlementRequest(long SellerId, long BankAccountId, long AmountIRR);
}