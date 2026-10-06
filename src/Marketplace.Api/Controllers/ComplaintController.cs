using Marketplace.Application.Complaints.Ports;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/complaints")]
public sealed class ComplaintController(IComplaintService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<long>> Open([FromBody] OpenComplaintRequest request, CancellationToken cancellationToken)
        => Ok(await service.OpenAsync(request.OrderId, request.CustomerId, request.Subject, request.Description, cancellationToken));

    public sealed record OpenComplaintRequest(long OrderId, long CustomerId, string Subject, string Description);
}