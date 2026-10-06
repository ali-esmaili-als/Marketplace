using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
namespace Marketplace.Application.Maintenance;
public sealed class MaintenanceService
{
 private readonly IMaintenanceRepository _repo; private readonly OrderLifecycleService _orders; private readonly IUnitOfWork _uow;
 public MaintenanceService(IMaintenanceRepository repo,OrderLifecycleService orders,IUnitOfWork uow){_repo=repo;_orders=orders;_uow=uow;}
 public async Task RunOnceAsync(CancellationToken ct=default)
 {
  var now=DateTime.UtcNow;
  foreach(var d in await _repo.GetExpiredDeliveriesAsync(now,ct))
  {
   try{await _orders.ExpireDeliveryAsync(d.OrderId,now,ct);}catch(DomainException){}
  }
  foreach(var r in await _repo.GetExpiredReservationsAsync(now,ct))
  {
   try{await _uow.ExecuteInTransactionAsync(async token=>{var inv=await _repo.GetInventoryItemAsync(r.ProductVariantId,token);if(inv is null||r.Status!=Marketplace.Domain.Inventory.InventoryReservationStatus.Active)return 0;inv.Release(r.Quantity);r.Expire(now);await _uow.SaveChangesAsync(token);return 0;},ct);}catch(DomainException){}
  }
  foreach(var o in await _repo.GetOrdersReadyToCompleteAsync(now,ct))
  {
   if(!await _repo.HasOpenComplaintAsync(o.Id,ct))
   {
    try{await _orders.CloseCompletedOrderAsync(o.Id,now,ct);}catch(DomainException){}
   }
  }
 }
}