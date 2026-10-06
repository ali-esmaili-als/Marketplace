using Marketplace.Application.Refunds.Ports;
using Marketplace.Domain.Refunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Marketplace.Api.Controllers;
[ApiController,Authorize,Route("api/refunds")]
public sealed class RefundController(IRefundService service):ControllerBase
{
 [HttpPost]
 public async Task<ActionResult<long>> Create(CreateRefundRequest request,CancellationToken ct)
  =>Ok(await service.CreateAsync(request.OrderId,request.PaymentId,request.AmountIRR,request.Reason,request.Items.Select(x=>new RefundLineRequest(x.OrderItemId,x.Quantity,x.InventoryDisposition)).ToArray(),ct));
 public sealed record CreateRefundRequest(long OrderId,long PaymentId,long AmountIRR,string? Reason,IReadOnlyList<RefundLineRequestDto> Items);
 public sealed record RefundLineRequestDto(long OrderItemId,int Quantity,RefundInventoryDisposition InventoryDisposition);
}