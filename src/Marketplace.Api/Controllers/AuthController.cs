using Marketplace.Infrastructure.Authorization;
using Marketplace.Application.Authorization;
using Marketplace.Application.Identity.Models;
using Marketplace.Application.Identity.Ports;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(
        LoginRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await auth.LoginAsync(
                request.Mobile,
                request.Password,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResult>> Refresh(
        RefreshRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await auth.RefreshAsync(
                request.RefreshToken,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [Authorize]
    [ActionAccess(UserTypeId.Admin, UserTypeId.Seller, UserTypeId.Customer)]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(
        RefreshRequest request,
        CancellationToken ct)
    {
        await auth.RevokeRefreshTokenAsync(
            request.RefreshToken,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        return NoContent();
    }
}
