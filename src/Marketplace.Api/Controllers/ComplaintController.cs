using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Authorization;
using Marketplace.Application.Complaints.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Marketplace.Api.Controllers;
[ApiController,Authorize,Route("api/complaints")]
public sealed class ComplaintController(IComplaintService service,ICurrentUser currentUser):ControllerBase
{
 [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Permission:"+PermissionCodes.ComplaintOpen)]
 [HttpPost]
 public async Task<ActionResult<long>> Open(OpenComplaintRequest request,CancellationToken ct)
 {
  if(!currentUser.IsAuthenticated)return Unauthorized();
  return Ok(await service.OpenAsync(request.OrderId,currentUser.UserId,request.Subject,request.Description,ct));
 }
 [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Permission:"+PermissionCodes.ComplaintResolveCustomer)]
 [HttpPost("{complaintId:long}/resolve/customer")] public async Task<IActionResult> ResolveForCustomer(long complaintId,CancellationToken ct){await service.ResolveForCustomerAsync(complaintId,ct);return NoContent();}
 [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Permission:"+PermissionCodes.ComplaintResolveSeller)]
 [HttpPost("{complaintId:long}/resolve/seller")] public async Task<IActionResult> ResolveForSeller(long complaintId,CancellationToken ct){await service.ResolveForSellerAsync(complaintId,ct);return NoContent();}
 [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Permission:"+PermissionCodes.ComplaintClose)]
 [HttpPost("{complaintId:long}/close")] public async Task<IActionResult> Close(long complaintId,CancellationToken ct){await service.CloseAsync(complaintId,ct);return NoContent();}
 public sealed record OpenComplaintRequest(long OrderId,string Subject,string Description);
}