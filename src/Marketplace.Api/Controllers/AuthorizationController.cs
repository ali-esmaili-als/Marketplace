using Marketplace.Infrastructure.Authorization;
using Marketplace.Application.Authorization;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController, Authorize]
[Route("api/authorization")]
public sealed class AuthorizationController(
    IAuthorizationCatalogReader catalog) : ControllerBase
{
    [ActionAccess(UserTypeId.Admin)]
    [HttpGet("actions")]
    public async Task<ActionResult<IReadOnlyList<AuthorizationActionDto>>> GetActions(
        CancellationToken cancellationToken)
        => Ok(await catalog.GetActionsAsync(cancellationToken));
}
