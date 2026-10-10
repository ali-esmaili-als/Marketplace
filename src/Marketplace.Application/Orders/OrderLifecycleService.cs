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
    private readonly TimeProvider _timeProvider;
    private static readonly TimeSpan ComplaintWindow = TimeSpan.FromDays(7);

    public OrderLifecycleService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids,INotificationRepository notifications,TimeProvider? timeProvider=null)
    {
        _orders=orders; _payments=payments; _life=life; _uow=uow; _ids=ids; _notifications=notifications;
        _timeProvider=timeProvider ?? TimeProvider.System;
    }

    private async Task NotifyAsync(long userId,string title,string body,long orderId,CancellationToken ct)
    {
        var notification=Notification.Create(await _ids.NextAsync(ct),userId,NotificationChannel.InApp,title,body,"Order",orderId);
        notification.MarkSent();
        _notifications.Add(notification);
    }

    private async Task NotifySellerAsync(long sellerId,string title,string body,long orderId,CancellationToken ct)
    {
        var userId=await _notifications.GetUserIdForSellerAsync(sellerId,ct);
        if(userId.HasValue)
            await NotifyAsync(userId.Value,title,body,orderId,ct);
    }

    public Task PaymentSucceededAsync(long orderId,string reference,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var p=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");

        if (p.Status == Marketplace.Domain.Payments.PaymentStatus.Succeeded)
        {
            // Idempotent callbacks are safe only after the order lifecycle has advanced
            // consistently. A successful payment attached to a pending/cancelled order
            // is a financial exception and must be reconciled, not silently ignored.
            if (o.Status is OrderStatus.PendingPayment or OrderStatus.Cancelled)
                throw new DomainException("Successful payment is inconsistent with the order status and requires reconciliation.");

            return 0;
        }

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

        await NotifyAsync(o.CustomerId,"پرداخت سفارش موفق بود",$"پرداخت سفارش شماره {o.Id} با موفقیت ثبت شد.",o.Id,token);
        await NotifySellerAsync(o.SellerId,"سفارش جدید دریافت شد",$"سفارش شماره {o.Id} پرداخت شده و برای آماده‌سازی در پنل شما قرار دارد.",o.Id,token);
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

        await NotifyAsync(o.CustomerId,"سفارش لغو شد",$"مهلت پرداخت سفارش شماره {o.Id} پایان یافت و سفارش لغو شد.",o.Id,token);
        await NotifySellerAsync(o.SellerId,"سفارش پرداخت نشد",$"سفارش شماره {o.Id} پس از پایان مهلت پرداخت لغو شد.",o.Id,token);
        await _uow.SaveChangesAsync(token);
        return true;
    },ct);

    public Task MarkReadyForDeliveryAsync(long orderId,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        // Seller actions may be retried after a timeout. Once the ready state and its
        // delivery code are committed, repeating the command must not issue a new code.
        if (o.Status == OrderStatus.ReadyForDelivery
            && d.Status == Marketplace.Domain.Delivery.DeliveryStatus.Ready
            && await _life.GetDeliveryCodeByOrderAsync(orderId,token) is not null)
            return 0;
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
        // Keep legacy parameters for caller compatibility, but never trust client/job supplied
        // timestamps for a financial transition or complaint deadline.
        _ = now;
        _ = complaintExpiresAtUtc;

        var invalidCode = await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var confirmedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var complaintDeadlineUtc = confirmedAtUtc.Add(ComplaintWindow);
            var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
            var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
            var code=await _life.GetDeliveryCodeByOrderAsync(orderId,token)??throw new DomainException("Delivery code not found.");
            if(!code.Verify(deliveryCode,confirmedAtUtc))
            {
                // Commit failed-attempt counters before returning the validation error.
                await _uow.SaveChangesAsync(token);
                return true;
            }

            d.ConfirmDelivered(confirmationReference,confirmedAtUtc);
            var pendingBefore=b.PendingIRR;
            _domain.OnDelivered(o,d,b,confirmedAtUtc,complaintDeadlineUtc);

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

            await NotifyAsync(o.CustomerId,"تحویل سفارش ثبت شد",$"تحویل سفارش شماره {o.Id} ثبت شد. مهلت ثبت شکایت طبق جزئیات سفارش محاسبه می‌شود.",o.Id,token);
            await NotifySellerAsync(o.SellerId,"تحویل سفارش ثبت شد",$"تحویل سفارش شماره {o.Id} ثبت شد.",o.Id,token);
            await _uow.SaveChangesAsync(token);
            return false;
        },ct);

        if(invalidCode)
            throw new DomainException("Invalid, expired, or locked delivery code.");
    }

    public Task ExpireDeliveryAsync(long orderId,DateTime now,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var o=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
        var d=await _life.GetDeliveryByOrderAsync(orderId,token)??throw new DomainException("Delivery not found.");
        // Expiry jobs are at-least-once. A previously committed expiry must not release the
        // seller's inventory reservation or alter financial buckets a second time.
        if(d.Status==Marketplace.Domain.Delivery.DeliveryStatus.Expired
            && o.Status is OrderStatus.DeliveryExpired or OrderStatus.RefundRequested)
            return 0;

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

        await NotifyAsync(o.CustomerId,"مهلت تحویل پایان یافت",$"مهلت تحویل سفارش شماره {o.Id} پایان یافت. وضعیت سفارش را در پنل پیگیری کنید.",o.Id,token);
        await NotifySellerAsync(o.SellerId,"مهلت تحویل پایان یافت",$"مهلت تحویل سفارش شماره {o.Id} پایان یافت.",o.Id,token);
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
        await NotifySellerAsync(o.SellerId,"شکایت جدید برای سفارش",$"برای سفارش شماره {o.Id} شکایت جدیدی ثبت شده است.",o.Id,token);
        await _uow.SaveChangesAsync(token);
        return c.Id;
    },ct);

    public Task ResolveComplaintAsync(long complaintId,bool customerWon,string note,CancellationToken ct=default)=>_uow.ExecuteInSerializableTransactionAsync(async token=>{
        var c=await _life.GetComplaintAsync(complaintId,token)??throw new DomainException("Complaint not found.");
        var o=await _orders.GetAsync(c.OrderId,token)??throw new DomainException("Order not found.");
        var b=await _life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");
        var h=await _life.GetActiveHoldByOrderAsync(o.Id,token)??throw new DomainException("Seller hold not found.");

        // The admin queue supports both Open and UnderReview complaints. Starting review
        // again on an already-reviewed complaint used to reject the visible resolution action.
        if(c.Status==ComplaintStatus.Open)
            c.StartReview();
        else if(c.Status!=ComplaintStatus.UnderReview)
            throw new DomainException("Only open or under-review complaints can be resolved.");

        if(customerWon)
        {
            c.ResolveForCustomer(note);
            _domain.OnCustomerWon(c,o);
        }
        else
        {
            c.ResolveForSeller(note);
            var blockedBefore=b.BlockedIRR;
            var availableBefore=b.AvailableIRR;
            _domain.OnSellerWon(c,o,b,h);
            // Releasing the complaint hold transfers value from Blocked to Available.
            // Record both sides so each bucket's ledger snapshot remains reconcilable.
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
                o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_SELLER_WON",BalanceBucket.Blocked));
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
                o.SellerAmountIRR,availableBefore,b.AvailableIRR,"COMPLAINT_SELLER_WON",BalanceBucket.Available));
        }

        await NotifyAsync(o.CustomerId,customerWon?"نتیجه شکایت به نفع شما ثبت شد":"شکایت به نفع فروشنده تعیین تکلیف شد",$"رسیدگی به شکایت سفارش شماره {o.Id} به پایان رسید.",o.Id,token);
        await NotifySellerAsync(o.SellerId,customerWon?"نتیجه شکایت به نفع خریدار":"شکایت به نفع شما تعیین تکلیف شد",$"رسیدگی به شکایت سفارش شماره {o.Id} به پایان رسید.",o.Id,token);
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
        var availableBefore=b.AvailableIRR;
        b.ReleaseBlock(o.SellerAmountIRR);
        h.Release();

        // A block release affects both buckets; keep both ledger snapshots in this transaction.
        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
            o.SellerAmountIRR,blockedBefore,b.BlockedIRR,"COMPLAINT_WINDOW_CLOSED",BalanceBucket.Blocked));
        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token),o.SellerId,o.Id,null,BalanceTransactionType.ComplaintHoldReleased,
            o.SellerAmountIRR,availableBefore,b.AvailableIRR,"COMPLAINT_WINDOW_CLOSED",BalanceBucket.Available));

        await NotifyAsync(o.CustomerId,"سفارش تکمیل شد",$"سفارش شماره {o.Id} تکمیل شد.",o.Id,token);
        await NotifySellerAsync(o.SellerId,"سفارش تکمیل شد",$"سفارش شماره {o.Id} تکمیل شد و دوره شکایت به پایان رسید.",o.Id,token);
        await _uow.SaveChangesAsync(token);
        return 0;
    },ct);
}
