using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Notifications;
using System.Security.Cryptography;

namespace Marketplace.Application.Orders;

public sealed class OrderLifecycleService
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly ILifecycleRepository _life;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly OrderFinancialLifecycle _domain=new();
    private readonly INotificationRepository _notifications;

    public OrderLifecycleService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids,INotificationRepository notifications)
    {
        _orders=orders; _payments=payments; _life=life; _uow=uow; _ids=ids; _notifications=notifications;
    }

    public Task PaymentSucceededAsync(long orderId,string reference,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var p=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        if(p.Status==Marketplace.Domain.Payments.PaymentStatus.Succeeded && o.Status!=OrderStatus.PendingPayment)
            return 0;

        p.Succeed(reference);
        var paymentTransaction=await _payments.GetLatestTransactionAsync(p.Id,token);
        if(paymentTransaction is not null && paymentTransaction.Status==Marketplace.Domain.Payments.PaymentTransactionStatus.Initiated)
            paymentTransaction.Succeed(reference);
        var hold=_domain.OnPaymentSucceeded(o,p,b,await _ids.NextAsync(token));
        _life.AddBalanceHold(hold);

        if(await _life.GetDeliveryByOrderAsync(o.Id,token) is null)
        {
            var expires=DateTime.UtcNow.AddDays(3);
            var delivery=Delivery.Create(await _ids.NextAsync(token),o.Id,o.SellerId,expires);
            o.SetDeliveryExpiry(expires);
            _life.AddDelivery(delivery);
            // A paid order must keep its inventory reserved for the entire delivery window.
            var reservations=await _life.GetReservationsByOrderAsync(o.Id,token);
            foreach(var reservation in reservations.Where(x=>x.Status==Marketplace.Domain.Inventory.InventoryReservationStatus.Active))
                reservation.ExtendExpiry(expires.AddMinutes(1));
        }

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.Sale,
            o.SellerAmountIRR,b.PendingIRR-o.SellerAmountIRR,b.PendingIRR,"PAYMENT",BalanceBucket.Pending));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task<bool> ExpirePendingPaymentAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        if(o.Status!=OrderStatus.PendingPayment) return false;

        var p=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
        if(p.Status is not (Marketplace.Domain.Payments.PaymentStatus.Pending or Marketplace.Domain.Payments.PaymentStatus.Redirected))
            return false;

        var reservations=await _life.GetReservationsByOrderAsync(orderId,token);
        if(!reservations.Any(x=>x.Status==Marketplace.Domain.Inventory.InventoryReservationStatus.Active && x.ExpiresAtUtc<=now))
            return false;

        o.Cancel();
        p.Fail();
        var transaction=await _payments.GetLatestTransactionAsync(p.Id,token);
        if(transaction?.Status==Marketplace.Domain.Payments.PaymentTransactionStatus.Initiated)
            transaction.Fail();

        foreach(var reservation in reservations.Where(x=>x.Status==Marketplace.Domain.Inventory.InventoryReservationStatus.Active))
        {
            var inventory=await _life.GetInventoryItemAsync(reservation.ProductVariantId,token)
                ??throw new DomainException("Inventory item not found.");
            inventory.Release(reservation.Quantity);
            reservation.Release();
        }

        await _uow.SaveChangesAsync(token);
        return true;
    },ct);

    public Task MarkReadyForDeliveryAsync(long orderId,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        if (o.Status == OrderStatus.Paid) o.StartPreparing();
        d.MarkReady();
        o.MarkReady();

        var deliveryCodeValue = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var deliveryCode = DeliveryCode.Create(
            await _ids.NextAsync(token),
            o.Id,
            deliveryCodeValue,
            d.ExpiresAtUtc);
        _life.AddDeliveryCode(deliveryCode);

        var notification = Notification.Create(
            await _ids.NextAsync(token),
            o.CustomerId,
            NotificationChannel.InApp,
            "کد تحویل سفارش",
            $"کد تحویل سفارش شما: {deliveryCodeValue}",
            "Order",
            o.Id);
        notification.MarkSent();
        _notifications.Add(notification);

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public async Task MarkDeliveredAsync(long orderId,string deliveryCode,string confirmationReference,DateTime now,DateTime complaintExpiresAtUtc,CancellationToken ct=default)
    {
        var invalidCode = await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
            var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
            var code=await _life.GetDeliveryCodeByOrderAsync(orderId,token)??throw new DomainException("Delivery code not found.");
            if(!code.Verify(deliveryCode,now))
            {
                // Commit failed-attempt counters before returning the validation error.
                await _uow.SaveChangesAsync(token);
                return true;
            }

            d.ConfirmDelivered(confirmationReference,now);
            var pendingBefore=b.PendingIRR;
            _domain.OnDelivered(o,d,b,now,complaintExpiresAtUtc);

            var reservations=await _life.GetReservationsByOrderAsync(o.Id,token);
            foreach(var reservation in reservations.Where(x=>x.Status==Marketplace.Domain.Inventory.InventoryReservationStatus.Active))
            {
                var inventory=await _life.GetInventoryItemAsync(reservation.ProductVariantId,token)??throw new DomainException("Inventory item not found.");
                inventory.ConsumeReservation(reservation.Quantity);
                reservation.Consume();
            }

            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.PendingReleased,
                o.SellerAmountIRR,pendingBefore,b.PendingIRR,"DELIVERY",BalanceBucket.Pending));

            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHold,
                o.SellerAmountIRR,b.BlockedIRR-o.SellerAmountIRR,b.BlockedIRR,"COMPLAINT_WINDOW",BalanceBucket.Blocked));

            await _uow.SaveChangesAsync(token);
            return false;
        },ct);

        if(invalidCode)
            throw new DomainException("Invalid, expired, or locked delivery code.");
    }

    public Task ExpireDeliveryAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        var pendingBefore=b.PendingIRR;
        d.Expire(now);
        _domain.OnDeliveryExpired(o,d,b,now);

        var reservations=await _life.GetReservationsByOrderAsync(o.Id,token);
        foreach(var reservation in reservations.Where(x=>x.Status==Marketplace.Domain.Inventory.InventoryReservationStatus.Active))
        {
            var inventory=await _life.GetInventoryItemAsync(reservation.ProductVariantId,token)??throw new DomainException("Inventory item not found.");
            inventory.Release(reservation.Quantity);
            reservation.Release();
        }

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.PendingRemoved,
            o.SellerAmountIRR,pendingBefore,b.PendingIRR,"DELIVERY_EXPIRED",BalanceBucket.Pending));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task<long> OpenComplaintAsync(long orderId,long customerId,string reason,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
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

    public Task ResolveComplaintAsync(long complaintId,bool customerWon,string note,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var c=await _life.GetComplaintAsync(complaintId,token)??throw new DomainException("Complaint not found.");
        var o=await _orders.GetAsync(c.OrderId,token)??throw new DomainException("Order not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
        var h=await _life.GetActiveHoldByOrderAsync(o.Id,token)??throw new DomainException("Seller hold not found.");

        c.StartReview();
        if(customerWon)
        {
            c.ResolveForCustomer(note);
            _domain.OnCustomerWon(c,o);
        }
        else
        {
            c.ResolveForSeller(note);
            var blockedBefore=b.BlockedIRR;
            _domain.OnSellerWon(c,o,b,h);
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
                o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_SELLER_WON",BalanceBucket.Blocked));
        }

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);

    public Task CloseCompletedOrderAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        if(await _life.GetOpenComplaintByOrderAsync(orderId,token)!=null)throw new DomainException("Order has an active complaint.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
        var h=await _life.GetActiveHoldByOrderAsync(orderId,token)??throw new DomainException("Seller hold not found.");

        // Preflight every cross-aggregate invariant before mutating the order, balance, or hold.
        // This also keeps the operation safe when the unit-of-work implementation is mocked
        // or when corrupted persisted data reaches the lifecycle service.
        if (b.SellerId != o.SellerId)
            throw new DomainException("Seller balance does not belong to this order's seller.");
        if (h.OrderId != o.Id || h.SellerId != o.SellerId || h.AmountIRR != o.SellerAmountIRR)
            throw new DomainException("Seller hold does not match the order and seller.");
        if (h.Status != BalanceHoldStatus.Active)
            throw new DomainException("Seller hold is not active.");
        if (b.BlockedIRR < o.SellerAmountIRR)
            throw new DomainException("Insufficient blocked seller balance.");

        // Complete validates the complaint-window deadline and order state before any money moves.
        o.Complete(now);
        var blockedBefore=b.BlockedIRR;
        b.ReleaseBlock(o.SellerAmountIRR);
        h.Release();

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
            o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_WINDOW_CLOSED",BalanceBucket.Blocked));

        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);
}
