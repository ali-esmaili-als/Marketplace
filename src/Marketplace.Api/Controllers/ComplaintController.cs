using Marketplace.Application.Complaints.Ports;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Api.Controllers;

[ApiController]
[Route("api/complaints")]
public sealed class ComplaintController(IComplaintService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<long>> Open(
        [FromBody] OpenComplaintRequest request, CancellationToken cancellationToken)
        => Ok(await service.OpenAsync(
            request.OrderId, request.CustomerId, request.Subject, request.Description, cancellationToken));

    [HttpPost("{complaintId:long}/resolve/customer")]
    public async Task<IActionResult> ResolveForCustomer(long complaintId, CancellationToken cancellationToken)
    {
        await service.ResolveForCustomerAsync(complaintId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{complaintId:long}/resolve/seller")]
    public async Task<IActionResult> ResolveForSeller(long complaintId, CancellationToken cancellationToken)
    {
        await service.ResolveForSellerAsync(complaintId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{complaintId:long}/close")]
    public async Task<IActionResult> Close(long complaintId, CancellationToken cancellationToken)
    {
        await service.CloseAsync(complaintId, cancellationToken);
        return NoContent();
    }

    public sealed record OpenComplaintRequest(
        long OrderId, long CustomerId, string Subject, string Description);
}