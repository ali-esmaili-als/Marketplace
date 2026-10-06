using System.Security.Claims;
using Marketplace.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
namespace Marketplace.Infrastructure.Authorization;
public sealed class PermissionHandler(IPermissionChecker checker):AuthorizationHandler<PermissionRequirement>
{
 protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,PermissionRequirement requirement)
 {
  var id=context.User.FindFirstValue(ClaimTypes.NameIdentifier);
  if(long.TryParse(id,out var userId)&&await checker.HasPermissionAsync(userId,requirement.Permission))
   context.Succeed(requirement);
 }
}