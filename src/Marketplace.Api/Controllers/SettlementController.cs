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

        var sellerId = request.SellerId ?? currentUser.UserId;
        if (sellerId != currentUser.UserId)
        {
            var isAdmin = User.IsInRole(UserTypeId.Admin.ToString());
            if (!isAdmin)
                return Forbid();
        }

        return Ok(await service.RequestAsync(
            sellerId,
            request.BankAccountId,
            request.AmountIRR,
            ct));
    }

    public sealed record RequestSettlementRequest(long BankAccountId, long AmountIRR, long? SellerId);
}
