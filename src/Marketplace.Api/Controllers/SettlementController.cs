using Marketplace.Infrastructure.Authorization;
using Marketplace.Application.Authorization;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController, Authorize]
[Route("api/settlements")]
public sealed class SettlementController(
    ISettlementService service,
    ICurrentUser currentUser) : ControllerBase
{
    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller)]
    [HttpPost]
    public async Task<ActionResult<long>> Request(
        RequestSettlementRequest request,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Unauthorized();

        return Ok(await service.RequestAsync(
            currentUser.UserId,
            request.BankAccountId,
            request.AmountIRR,
            ct));
    }

    public sealed record RequestSettlementRequest(long BankAccountId, long AmountIRR);
}
