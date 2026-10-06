using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("version")]
    public IActionResult Version() => Ok(new { service = "Marketplace.Api", status = "ok" });
}