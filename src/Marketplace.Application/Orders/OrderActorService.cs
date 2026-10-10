using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;
namespace Marketplace.Application.Orders;
public sealed class OrderActorService
{
 private readonly ISellerManagementRepository _sellers; private readonly IOrderRepository _orders; private readonly OrderLifecycleService _lifecycle; private readonly RefundService _refunds;
 public OrderActorService(ISellerManagementRepository sellers,IOrderRepository orders,OrderLifecycleService lifecycle,RefundService refunds){_sellers=sellers;_orders=orders;_lifecycle=lifecycle;_refunds=refunds;}
 private async Task<long> SellerId(long userId,CancellationToken ct)=> (await _sellers.GetSellerByUserIdAsync(userId,ct)??throw new DomainException("Seller profile not found.")).Id;
 private async Task<Order> Own(long orderId,long userId,bool seller,CancellationToken ct){var o=await _orders.GetAsync(orderId,ct)??throw new DomainException("Order not found.");var sid=seller?await SellerId(userId,ct):0;if(seller?o.SellerId!=sid:o.CustomerId!=userId)throw new DomainException("You do not own this order.");return o;}
 public async Task ReadyAsync(long userId,long orderId,CancellationToken ct=default){await Own(orderId,userId,true,ct);await _lifecycle.MarkReadyForDeliveryAsync(orderId,ct);}
 public async Task DeliverAsync(long userId,long orderId,string code,string reference,DateTime deliveredAt,DateTime complaintExpires,CancellationToken ct=default)
 {
  await Own(orderId,userId,true,ct);
  // The request timestamp is client-controlled. Never use it to verify code expiry/lockout
  // or to move delivery and financial state backwards/forwards in time.
  await _lifecycle.MarkDeliveredAsync(orderId,code,reference,DateTime.UtcNow,complaintExpires,ct);
 }
 public async Task ExpireAsync(long userId,long orderId,CancellationToken ct=default){await Own(orderId,userId,true,ct);await _lifecycle.ExpireDeliveryAsync(orderId,DateTime.UtcNow,ct);}
 public async Task<long> ComplaintAsync(long userId,long orderId,string reason,CancellationToken ct=default){await Own(orderId,userId,false,ct);return await _lifecycle.OpenComplaintAsync(orderId,userId,reason,ct);}
 public async Task RefundAsync(long userId,long orderId,RefundReason reason,CancellationToken ct=default){await Own(orderId,userId,false,ct);await _refunds.ProcessAsync(orderId,reason,ct);}
}