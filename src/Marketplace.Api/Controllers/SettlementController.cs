using Marketplace.Application.Authorization;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Authorization;
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

        return Ok(await service.RequestAsync(
            sellerId,
            request.BankAccountId,
            request.AmountIRR,
            ct));
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{settlementId:long}/processing")]
    public async Task<IActionResult> MarkProcessing(
        long settlementId,
        CancellationToken ct)
    {
        await service.MarkProcessingAsync(settlementId, ct);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{settlementId:long}/complete")]
    public async Task<IActionResult> Complete(
        long settlementId,
        CompleteSettlementRequest request,
        CancellationToken ct)
    {
        await service.CompleteAsync(
            settlementId,
            request.GatewayReference,
            ct);

        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("{settlementId:long}/fail")]
    public async Task<IActionResult> Fail(
        long settlementId,
        FailSettlementRequest request,
        CancellationToken ct)
    {
        await service.FailAsync(
            settlementId,
            request.Reason,
            ct);

        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller)]
    [HttpPost("{settlementId:long}/cancel")]
    public async Task<IActionResult> Cancel(
        long settlementId,
        CancellationToken ct)
    {
        await service.CancelAsync(settlementId, ct);
        return NoContent();
    }

    public sealed record RequestSettlementRequest(
        long BankAccountId,
        long AmountIRR,
        long? SellerId);

    public sealed record CompleteSettlementRequest(string GatewayReference);

    public sealed record FailSettlementRequest(string Reason);
}
