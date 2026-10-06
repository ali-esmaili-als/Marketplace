using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;

namespace Marketplace.Application.Orders;

public sealed class OrderLifecycleService
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly ILifecycleRepository _life;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly OrderFinancialLifecycle _domain=new();

    public OrderLifecycleService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids)
    {
        _orders=orders; _payments=payments; _life=life; _uow=uow; _ids=ids;
    }

    public Task PaymentSucceededAsync(long orderId,string reference,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var p=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        if(p.Status==Marketplace.Domain.Payments.PaymentStatus.Succeeded && o.Status!=OrderStatus.PendingPayment)
            return 0;

        p.Succeed(reference);
        var hold=_domain.OnPaymentSucceeded(o,p,b,await _ids.NextAsync(token));
        _life.AddBalanceHold(hold);

        if(await _life.GetDeliveryByOrderAsync(o.Id,token) is null)
        {
            var expires=DateTime.UtcNow.AddDays(7);
            var delivery=Delivery.Create(await _ids.NextAsync(token),o.Id,o.SellerId,expires);
            o.SetDeliveryExpiry(expires);
            _life.AddDelivery(delivery);
        }

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.Sale,
            o.SellerAmountIRR,b.PendingIRR-o.SellerAmountIRR,b.PendingIRR,"PAYMENT"));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task MarkReadyForDeliveryAsync(long orderId,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        o.StartPreparing();
        d.MarkReady();
        o.MarkReady();
        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task MarkDeliveredAsync(long orderId,string confirmationReference,DateTime now,DateTime complaintExpiresAtUtc,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        d.ConfirmDelivered(confirmationReference,now);
        var pendingBefore=b.PendingIRR;
        _domain.OnDelivered(o,d,b,now,complaintExpiresAtUtc);

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.PendingReleased,
            o.SellerAmountIRR,pendingBefore,b.PendingIRR,"DELIVERY"));

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHold,
            o.SellerAmountIRR,b.BlockedIRR-o.SellerAmountIRR,b.BlockedIRR,"COMPLAINT_WINDOW"));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task ExpireDeliveryAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        var pendingBefore=b.PendingIRR;
        d.Expire(now);
        _domain.OnDeliveryExpired(o,d,b);

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.PendingRemoved,
            o.SellerAmountIRR,pendingBefore,b.PendingIRR,"DELIVERY_EXPIRED"));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task<long> OpenComplaintAsync(long orderId,long customerId,string reason,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        if(o.CustomerId!=customerId)throw new DomainException("Customer does not own this order.");
        if(o.Status!=OrderStatus.Delivered||o.ComplaintExpiresAtUtc is null||DateTime.UtcNow>o.ComplaintExpiresAtUtc.Value)
            throw new DomainException("Complaint window is closed.");
        if(await _life.GetOpenComplaintByOrderAsync(orderId,token)!=null)throw new DomainException("An active complaint already exists.");

        var c=Complaint.Create(await _ids.NextAsync(token),o.Id,customerId,o.SellerId,reason);
        _life.AddComplaint(c);
        await _uow.SaveChangesAsync(token);
        return c.Id;
    },ct);

    public Task ResolveComplaintAsync(long complaintId,bool customerWon,string note,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var c=await _life.GetComplaintAsync(complaintId,token)??throw new DomainException("Complaint not found.");
        var o=await _orders.GetAsync(c.OrderId,token)??throw new DomainException("Order not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
        var h=await _life.GetActiveHoldByOrderAsync(o.Id,token)??throw new DomainException("Seller hold not found.");

        c.StartReview();
        if(customerWon)
        {
            c.ResolveForCustomer(note);
            o.RequestRefund();
        }
        else
        {
            c.ResolveForSeller(note);
            var blockedBefore=b.BlockedIRR;
            _domain.OnSellerWon(c,o,b,h);
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
                o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_SELLER_WON"));
        }

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task CloseCompletedOrderAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        if(await _life.GetOpenComplaintByOrderAsync(orderId,token)!=null)throw new DomainException("Order has an active complaint.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
        var h=await _life.GetActiveHoldByOrderAsync(orderId,token)??throw new DomainException("Seller hold not found.");

        var blockedBefore=b.BlockedIRR;
        o.Complete(now);
        b.ReleaseBlock(o.SellerAmountIRR);
        h.Release();

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
            o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_WINDOW_CLOSED"));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);
}
