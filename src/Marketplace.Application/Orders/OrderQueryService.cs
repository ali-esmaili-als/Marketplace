using Marketplace.Application.Abstractions;
namespace Marketplace.Application.Orders;
public sealed class OrderQueryService
{
 private readonly IOrderQueryRepository _repo;
 public OrderQueryService(IOrderQueryRepository repo)=>_repo=repo;
 private static OrderSummary Map(Marketplace.Domain.Orders.Order o)=>new(o.Id,o.StoreId,o.SellerId,o.CustomerId,o.Status,o.SubtotalAmountIRR,o.CampaignDiscountIRR,o.CouponDiscountIRR,o.TotalAmountIRR,o.CouponCodeSnapshot,o.CreatedAtUtc);
 public async Task<IReadOnlyList<OrderSummary>> GetCustomerOrdersAsync(long customerId,CancellationToken ct=default)=>(await _repo.GetCustomerOrdersAsync(customerId,ct)).Select(Map).ToList();
 public async Task<IReadOnlyList<OrderSummary>> GetSellerOrdersAsync(long sellerId,CancellationToken ct=default)=>(await _repo.GetSellerOrdersAsync(sellerId,ct)).Select(Map).ToList();
 public async Task<OrderDetails> GetCustomerOrderAsync(long customerId,long orderId,CancellationToken ct=default){var o=await _repo.GetAsync(orderId,ct)??throw new Marketplace.Domain.Common.DomainException("Order not found.");if(o.CustomerId!=customerId)throw new Marketplace.Domain.Common.DomainException("Customer does not own this order.");return await Details(o,ct);}
 public async Task<OrderDetails> GetSellerOrderAsync(long sellerId,long orderId,CancellationToken ct=default){var o=await _repo.GetAsync(orderId,ct)??throw new Marketplace.Domain.Common.DomainException("Order not found.");if(o.SellerId!=sellerId)throw new Marketplace.Domain.Common.DomainException("Seller does not own this order.");return await Details(o,ct);}
 private async Task<OrderDetails> Details(Marketplace.Domain.Orders.Order o,CancellationToken ct){var p=await _repo.GetPaymentAsync(o.Id,ct);var items=(await _repo.GetItemsAsync(o.Id,ct)).Select(x=>new OrderItemSnapshot(x.ProductId,x.VariantId,x.ProductNameSnapshot,x.Quantity,x.BaseUnitPriceIRR,x.CampaignDiscountIRR,x.CouponDiscountIRR,x.WarrantyPriceIRR,x.LineTotalIRR)).ToList();return new OrderDetails(Map(o),items,p?.Status.ToString(),p?.ReferenceNumber,o.DeliveredAtUtc,o.ComplaintExpiresAtUtc);}
}