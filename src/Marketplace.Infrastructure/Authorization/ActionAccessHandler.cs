using System.Security.Claims;
using Marketplace.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Marketplace.Infrastructure.Authorization;

public sealed class ActionAccessHandler(
    IPermissionChecker checker,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(id, out var userId))
            return;

        var action = httpContextAccessor.HttpContext?
            .GetEndpoint()?
            .Metadata
            .GetMetadata<ControllerActionDescriptor>();

        if (action is null)
            return;

        var permission = action.ControllerName + "." + action.ActionName;

        if (await checker.HasPermissionAsync(userId, permission, context.CancellationToken))
            context.Succeed(requirement);
    }
}
