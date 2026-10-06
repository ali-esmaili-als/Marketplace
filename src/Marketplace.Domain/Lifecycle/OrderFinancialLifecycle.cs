using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;

namespace Marketplace.Domain.Lifecycle;

public sealed class OrderFinancialLifecycle
{
    public void OnPaymentSucceeded(Order order, Payment payment, SellerBalance balance)
    {
        if(payment.Status!=PaymentStatus.Succeeded) throw new DomainException("Payment must be successful.");
        order.MarkPaid();
        balance.AddPending(order.SellerAmountIRR);
    }

    public void OnDelivered(Order order, Delivery delivery, SellerBalance balance, SellerBalanceHold hold, DateTime now, DateTime complaintExpiresAtUtc)
    {
        delivery.ConfirmDelivered(delivery.ConfirmationReference ?? "SYSTEM", now);
        order.MarkDelivered(now, complaintExpiresAtUtc);
        balance.ReleasePending(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        hold.Release(); // hold object is only a financial audit marker; actual amount is blocked on balance.
    }

    public void OnDeliveryExpired(Order order, Delivery delivery)
    {
        delivery.Expire(DateTime.UtcNow);
        order.MarkDeliveryExpired(DateTime.UtcNow);
        order.RequestRefund();
    }

    public Refund OpenRefund(Order order, Payment payment, long refundId, RefundReason reason)
    {
        if(order.Status!=OrderStatus.RefundRequested) throw new DomainException("Order is not awaiting refund.");
        return Refund.Create(refundId,order.Id,payment.Id,order.CustomerId,order.TotalAmountIRR,reason);
    }

    public void OnCustomerWon(Complaint complaint, Order order)
    {
        if(complaint.Status!=ComplaintStatus.CustomerWon) throw new DomainException("Complaint is not resolved for customer.");
        order.RequestRefund();
    }

    public void OnSellerWon(Complaint complaint, Order order, SellerBalance balance, SellerBalanceHold hold)
    {
        if(complaint.Status!=ComplaintStatus.SellerWon) throw new DomainException("Complaint is not resolved for seller.");
        balance.ReleaseBlock(order.SellerAmountIRR);
        hold.Release();
        order.Complete(DateTime.UtcNow);
    }

    public void CompleteRefund(Order order, Payment payment, Refund refund, SellerBalance balance, SellerBalanceHold hold)
    {
        if(refund.Status!=RefundStatus.Completed) throw new DomainException("Refund is not completed.");
        payment.MarkRefunded();
        if(balance.BlockedIRR>=order.SellerAmountIRR) balance.ConsumeBlock(order.SellerAmountIRR);
        hold.Consume();
        order.MarkRefunded();
    }
}