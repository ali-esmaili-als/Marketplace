using Marketplace.Application.Abstractions;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Orders;

public sealed class OrderLifecycleService
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly ILifecycleRepository _lifecycle;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly OrderFinancialLifecycle _domainLifecycle = new();

    public OrderLifecycleService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository lifecycle,IUnitOfWork uow,IIdGenerator ids)
    { _orders=orders;_payments=payments;_lifecycle=lifecycle;_uow=uow;_ids=ids; }

    public async Task PaymentSucceededAsync(long orderId,string reference,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token=>{
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var payment=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
            var balance=await _lifecycle.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            payment.Succeed(reference);
            var hold=_domainLifecycle.OnPaymentSucceeded(order,payment,balance,await _ids.NextAsync(token));
            _lifecycle.AddBalanceTransaction(BalanceTransaction.Create(await _ids.NextAsync(token),order.SellerId,order.Id,null,BalanceTransactionType.Sale,order.SellerAmountIRR,balance.PendingIRR-order.SellerAmountIRR,balance.PendingIRR,"PAYMENT"));
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);
    }

    public async Task MarkDeliveredAsync(long orderId,string confirmationReference,DateTime now,DateTime complaintExpiresAtUtc,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token=>{
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var delivery=await _lifecycle.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
            var balance=await _lifecycle.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            delivery.ConfirmDelivered(confirmationReference,now);
            _domainLifecycle.OnDelivered(order,delivery,balance,now,complaintExpiresAtUtc);
            await _uow.SaveChangesAsync(token); return 0;
        },ct);
    }

    public async Task ExpireDeliveryAsync(long orderId,DateTime now,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token=>{
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var delivery=await _lifecycle.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
            var balance=await _lifecycle.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            delivery.Expire(now);
            _domainLifecycle.OnDeliveryExpired(order,delivery,balance);
            await _uow.SaveChangesAsync(token); return 0;
        },ct);
    }

    public async Task<long> OpenComplaintAsync(long orderId,long customerId,string reason,CancellationToken ct=default)
    {
        return await _uow.ExecuteInTransactionAsync(async token=>{
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            if(order.CustomerId!=customerId)throw new DomainException("Customer does not own this order.");
            if(order.Status!=Domain.Orders.OrderStatus.Delivered||order.ComplaintExpiresAtUtc is null||DateTime.UtcNow>order.ComplaintExpiresAtUtc.Value)
                throw new DomainException("Complaint window is closed.");
            if(await _lifecycle.GetOpenComplaintByOrderAsync(orderId,token)!=null)throw new DomainException("An active complaint already exists.");
            var id=await _ids.NextAsync(token);_lifecycle.AddComplaint(Complaint.Create(id,order.Id,customerId,order.SellerId,reason));
            await _uow.SaveChangesAsync(token);return id;
        },ct);
    }

    public async Task CustomerWonAsync(long complaintId,string note,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token=>{
            throw new NotSupportedException("Resolve by complaint id requires a complaint repository in the next application slice.");
#pragma warning disable CS0162
            return 0;
#pragma warning restore CS0162
        },ct);
    }

    public async Task CloseCompletedOrderAsync(long orderId,DateTime now,CancellationToken ct=default)
    {
        await _uow.ExecuteInTransactionAsync(async token=>{
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            if(await _lifecycle.GetOpenComplaintByOrderAsync(orderId,token)!=null)throw new DomainException("Order has an active complaint.");
            var balance=await _lifecycle.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            var hold=await _lifecycle.GetActiveHoldByOrderAsync(orderId,token)??throw new DomainException("Seller hold not found.");
            order.Complete(now);balance.ReleaseBlock(order.SellerAmountIRR);hold.Release();
            await _uow.SaveChangesAsync(token);return 0;
        },ct);
    }
}