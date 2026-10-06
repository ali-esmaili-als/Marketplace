using Marketplace.Application.Authorization;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController, Authorize]
[Route("api/authorization/admin")]
public sealed class AuthorizationAdminController(
    IAuthorizationAdminService service) : ControllerBase
{
    [ActionAccess(UserTypeId.Admin)]
    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<AuthorizationRuleDto>>> GetRules(
        CancellationToken ct)
        => Ok(await service.GetRulesAsync(ct));

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("rules")]
    public async Task<ActionResult<long>> CreateRule(
        CreateRuleRequest request,
        CancellationToken ct)
        => Ok(await service.CreateRuleAsync(
            request.Code,
            request.Name,
            request.RuleType,
            request.PermissionIds,
            ct));

    [ActionAccess(UserTypeId.Admin)]
    [HttpPut("rules/{ruleId:long}/permissions")]
    public async Task<IActionResult> UpdateRulePermissions(
        long ruleId,
        UpdateRulePermissionsRequest request,
        CancellationToken ct)
    {
        await service.UpdateRulePermissionsAsync(ruleId, request.PermissionIds, ct);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpGet("users/{userId:long}/rules")]
    public async Task<ActionResult<UserAuthorizationRulesDto>> GetUserRules(
        long userId,
        CancellationToken ct)
        => Ok(await service.GetUserRulesAsync(userId, ct));

    [ActionAccess(UserTypeId.Admin)]
    [HttpPost("users/{userId:long}/rules")]
    public async Task<IActionResult> AssignRule(
        long userId,
        AssignRuleRequest request,
        CancellationToken ct)
    {
        if (request.UserId != userId)
            return BadRequest("User id mismatch.");

        await service.AssignRuleAsync(userId, request.RuleId, ct);
        return NoContent();
    }

    [ActionAccess(UserTypeId.Admin)]
    [HttpDelete("users/{userId:long}/rules/{ruleId:long}")]
    public async Task<IActionResult> RemoveRule(
        long userId,
        long ruleId,
        CancellationToken ct)
    {
        await service.RemoveRuleAsync(userId, ruleId, ct);
        return NoContent();
    }
}
