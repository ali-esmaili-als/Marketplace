using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;

namespace Marketplace.Domain.Lifecycle;

public sealed class OrderFinancialLifecycle
{
    public SellerBalanceHold OnPaymentSucceeded(Order order, Payment payment, SellerBalance balance, long holdId)
    {
        if (payment.Status != PaymentStatus.Succeeded)
            throw new DomainException("Payment must be successful.");
        if (payment.OrderId != order.Id || payment.CustomerId != order.CustomerId)
            throw new DomainException("Payment does not belong to this order and customer.");
        if (payment.AmountIRR != order.TotalAmountIRR)
            throw new DomainException("Payment amount does not match the order total.");
        if (order.SellerAmountIRR <= 0)
            throw new DomainException("Seller share must be positive before payment can be finalized.");
        if (balance.SellerId != order.SellerId)
            throw new DomainException("Seller balance does not belong to this order's seller.");
        if (holdId <= 0)
            throw new DomainException("A valid balance hold ID is required.");

        // Validate all cross-aggregate invariants before mutating any aggregate.
        var hold = SellerBalanceHold.Create(holdId, order.SellerId, order.Id, order.SellerAmountIRR,
            "Secure payment pending delivery and complaint window");
        order.MarkPaid();
        balance.AddPending(order.SellerAmountIRR);
        return hold;
    }

    public void OnDelivered(Order order, DeliveryEntity delivery, SellerBalance balance, DateTime now, DateTime complaintExpiresAtUtc)
    {
        if (delivery.Status != DeliveryStatus.Delivered)
            throw new DomainException("Delivery must be confirmed.");
        if (delivery.OrderId != order.Id || delivery.SellerId != order.SellerId || balance.SellerId != order.SellerId)
            throw new DomainException("Delivery and balance must belong to the order's seller.");
        if (balance.PendingIRR < order.SellerAmountIRR)
            throw new DomainException("Insufficient pending seller balance.");
        if (complaintExpiresAtUtc <= now)
            throw new DomainException("Complaint expiry must be after delivery.");

        // Preflight all checks before changing order or balance buckets.
        order.MarkDelivered(now, complaintExpiresAtUtc);
        balance.ReleasePending(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
    }

    public void OnDeliveryExpired(Order order, DeliveryEntity delivery, SellerBalance balance, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (delivery.Status != DeliveryStatus.Expired)
            throw new DomainException("Delivery must be expired.");
        if (delivery.OrderId != order.Id || delivery.SellerId != order.SellerId || balance.SellerId != order.SellerId)
            throw new DomainException("Delivery and balance must belong to the order's seller.");
        if (order.Status != OrderStatus.ReadyForDelivery || order.DeliveryExpiresAtUtc is null || now < order.DeliveryExpiresAtUtc.Value)
            throw new DomainException("Order delivery has not expired.");
        if (balance.PendingIRR < order.SellerAmountIRR)
            throw new DomainException("Insufficient pending seller balance.");

        order.MarkDeliveryExpired(now);
        order.RequestRefund();
        balance.RemovePending(order.SellerAmountIRR);
    }

    public Refund OpenRefund(Order order, Payment payment, long refundId, RefundReason reason)
    {
        if (order.Status != OrderStatus.RefundRequested)
            throw new DomainException("Order is not awaiting refund.");
        if (payment.OrderId != order.Id || payment.CustomerId != order.CustomerId)
            throw new DomainException("Payment does not belong to this order and customer.");
        if (payment.AmountIRR != order.TotalAmountIRR)
            throw new DomainException("Payment amount does not match the order total.");
        if (payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.ReconciliationRequired))
            throw new DomainException("Payment is not eligible for refund.");
        return Refund.Create(refundId, order.Id, payment.Id, order.CustomerId, order.TotalAmountIRR, reason);
    }

    public void OnCustomerWon(Complaint complaint, Order order)
    {
        if (complaint.Status != ComplaintStatus.CustomerWon)
            throw new DomainException("Complaint is not resolved for customer.");
        if (complaint.OrderId != order.Id || complaint.CustomerId != order.CustomerId ||
            complaint.SellerId != order.SellerId)
            throw new DomainException("Complaint does not belong to this order and its parties.");
        order.RequestRefund();
    }

    public void OnSellerWon(Complaint complaint, Order order, SellerBalance balance, SellerBalanceHold hold)
    {
        if (complaint.Status != ComplaintStatus.SellerWon)
            throw new DomainException("Complaint is not resolved for seller.");
        if (complaint.OrderId != order.Id || complaint.CustomerId != order.CustomerId ||
            complaint.SellerId != order.SellerId)
            throw new DomainException("Complaint does not belong to this order and its parties.");
        if (balance.SellerId != order.SellerId || hold.SellerId != order.SellerId ||
            hold.OrderId != order.Id || hold.AmountIRR != order.SellerAmountIRR)
            throw new DomainException("Balance hold does not match the order and seller.");
        if (hold.Status != BalanceHoldStatus.Active)
            throw new DomainException("Balance hold is not active.");
        if (balance.BlockedIRR < order.SellerAmountIRR)
            throw new DomainException("Insufficient blocked seller balance.");
        if (order.Status != OrderStatus.Delivered || order.ComplaintExpiresAtUtc is null ||
            DateTime.UtcNow < order.ComplaintExpiresAtUtc.Value)
            throw new DomainException("Complaint period has not expired.");

        // All state and balance preconditions are checked before any mutation.
        order.Complete(DateTime.UtcNow);
        balance.ReleaseBlock(order.SellerAmountIRR);
        hold.Release();
    }

    public void CompleteRefund(Order order, Payment payment, Refund refund, SellerBalance balance, SellerBalanceHold hold)
    {
        if (refund.Status != RefundStatus.Completed)
            throw new DomainException("Refund is not completed.");
        if (order.Status != OrderStatus.RefundRequested)
            throw new DomainException("Order is not awaiting refund.");
        if (refund.OrderId != order.Id || refund.PaymentId != payment.Id ||
            refund.CustomerId != order.CustomerId || refund.AmountIRR != order.TotalAmountIRR)
            throw new DomainException("Refund does not match the order and payment.");
        if (payment.OrderId != order.Id || payment.CustomerId != order.CustomerId ||
            payment.AmountIRR != order.TotalAmountIRR ||
            payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded or PaymentStatus.ReconciliationRequired))
            throw new DomainException("Payment is not eligible for this refund.");
        if (balance.SellerId != order.SellerId || hold.SellerId != order.SellerId ||
            hold.OrderId != order.Id || hold.AmountIRR != order.SellerAmountIRR)
            throw new DomainException("Balance hold does not match the order and seller.");
        if (hold.Status != BalanceHoldStatus.Active)
            throw new DomainException("Balance hold is not active.");
        var sellerFundsAvailable = balance.BlockedIRR >= order.SellerAmountIRR ||
                                   balance.PendingIRR >= order.SellerAmountIRR;
        if (!sellerFundsAvailable && refund.Reason != RefundReason.DeliveryExpired)
            throw new DomainException("Insufficient seller funds to complete refund.");

        // Delivery-expiry processing may already have removed the seller's pending share.
        // Never debit another bucket in that case; the platform refund is reconciled separately.
        payment.MarkRefunded();
        if (balance.BlockedIRR >= order.SellerAmountIRR)
            balance.ConsumeBlock(order.SellerAmountIRR);
        else if (balance.PendingIRR >= order.SellerAmountIRR)
            balance.RemovePending(order.SellerAmountIRR);
        hold.Consume();
        order.MarkRefunded();
    }
}