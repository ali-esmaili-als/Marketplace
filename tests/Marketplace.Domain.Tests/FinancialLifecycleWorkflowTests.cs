using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class FinancialLifecycleWorkflowTests
{
    [Fact]
    public void CustomerWinsComplaint_CompletesRefundAndConsumesBlockedSellerFundsOnce()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(1, 10, 20, 30, 1_000_000, 1_000_000);
        var payment = Payment.Create(2, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("PAYMENT-REF");
        var balance = SellerBalance.Create(3, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 4);

        order.MarkReady();
        var delivery = DeliveryEntity.Create(5, order.Id, order.SellerId, now.AddDays(1));
        delivery.MarkReady();
        delivery.ConfirmDelivered("DELIVERY-REF", now);
        lifecycle.OnDelivered(order, delivery, balance, now, now.AddDays(3));

        var complaint = Complaint.Create(6, order.Id, order.CustomerId, order.SellerId, "Product damaged");
        complaint.StartReview();
        complaint.ResolveForCustomer("Evidence confirms damage");
        lifecycle.OnCustomerWon(complaint, order);

        var refund = lifecycle.OpenRefund(order, payment, 7, RefundReason.ComplaintCustomerWon);
        refund.Approve();
        refund.Complete("REFUND-REF");
        lifecycle.CompleteRefund(order, payment, refund, balance, hold);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(RefundStatus.Completed, refund.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(BalanceHoldStatus.Consumed, hold.Status);
    }

    [Fact]
    public void DeliveryExpiredRefund_DoesNotDebitSellerFundsTwice()
    {
        var order = Order.Create(11, 12, 13, 14, 2_500_000, 2_500_000);
        var payment = Payment.Create(15, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("PAYMENT-EXP");
        var balance = SellerBalance.Create(16, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 17);

        order.MarkReady();
        var expiry = DateTime.UtcNow.AddMinutes(10);
        order.SetDeliveryExpiry(expiry);
        var delivery = DeliveryEntity.Create(18, order.Id, order.SellerId, expiry);
        delivery.MarkReady();
        delivery.Expire(expiry.AddSeconds(1));
        lifecycle.OnDeliveryExpired(order, delivery, balance, expiry.AddSeconds(1));

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(0, balance.PendingIRR);

        var refund = lifecycle.OpenRefund(order, payment, 19, RefundReason.DeliveryExpired);
        refund.Approve();
        refund.Complete("REFUND-EXP");
        lifecycle.CompleteRefund(order, payment, refund, balance, hold);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(BalanceHoldStatus.Consumed, hold.Status);
    }

    [Fact]
    public void OpenRefund_RejectsPaymentFromAnotherOrderWithoutChangingOrder()
    {
        var order = Order.Create(21, 22, 23, 24, 500_000, 500_000);
        order.MarkPaid();
        order.MarkReady();
        var deliveredAt = DateTime.UtcNow;
        order.MarkDelivered(deliveredAt, deliveredAt.AddDays(1));
        order.RequestRefund();
        var unrelatedPayment = Payment.Create(25, 999, order.CustomerId, order.TotalAmountIRR);
        unrelatedPayment.Succeed("UNRELATED");

        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<DomainException>(() =>
            lifecycle.OpenRefund(order, unrelatedPayment, 26, RefundReason.AdminAdjustment));
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, unrelatedPayment.Status);
    }

    [Fact]
    public void CustomerWonComplaintForAnotherOrderCannotRequestRefund()
    {
        var order = Order.Create(31, 32, 33, 34, 500_000, 500_000);
        var complaint = Complaint.Create(35, 999, order.CustomerId, order.SellerId, "Wrong order");
        complaint.StartReview();
        complaint.ResolveForCustomer("Reviewed");

        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<DomainException>(() => lifecycle.OnCustomerWon(complaint, order));
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void CompleteRefund_RejectsRefundForDifferentOrderWithoutMutatingPaymentOrBalance()
    {
        var order = Order.Create(41, 42, 43, 44, 800_000, 800_000);
        var payment = Payment.Create(45, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("PAYMENT");
        var balance = SellerBalance.Create(46, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 47);
        order.MarkReady();
        var now = DateTime.UtcNow;
        var delivery = DeliveryEntity.Create(48, order.Id, order.SellerId, now.AddDays(1));
        delivery.MarkReady();
        delivery.ConfirmDelivered("DELIVERY", now);
        lifecycle.OnDelivered(order, delivery, balance, now, now.AddDays(2));
        order.RequestRefund();

        var refund = Refund.Create(49, 999, payment.Id, order.CustomerId, order.TotalAmountIRR, RefundReason.AdminAdjustment);
        refund.Approve();
        refund.Complete("REFUND");

        Assert.Throws<DomainException>(() => lifecycle.CompleteRefund(order, payment, refund, balance, hold));
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(800_000, balance.BlockedIRR);
        Assert.Equal(BalanceHoldStatus.Active, hold.Status);
    }
}
