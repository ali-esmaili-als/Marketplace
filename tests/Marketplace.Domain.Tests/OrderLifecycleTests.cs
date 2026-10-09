using System;
using Marketplace.Domain.Complaints;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class OrderLifecycleTests
{
    [Fact]
    public void SuccessfulPayment_MovesSellerMoneyToPendingAndCreatesHold()
    {
        var order=Order.Create(1,10,20,30,1_000_000,1_000_000);
        var payment=Payment.Create(2,1,10,1_000_000);
        payment.Succeed("REF-1");
        var balance=SellerBalance.Create(3,20);
        var lifecycle=new OrderFinancialLifecycle();

        var hold=lifecycle.OnPaymentSucceeded(order,payment,balance,4);

        Assert.Equal(OrderStatus.Paid,order.Status);
        Assert.Equal(1_000_000,balance.PendingIRR);
        Assert.Equal(1_000_000,hold.AmountIRR);
        Assert.Equal(BalanceHoldStatus.Active,hold.Status);
    }

    [Fact]
    public void DuplicateSuccessfulPayment_DoesNotCreditSellerBalanceTwice()
    {
        var order = Order.Create(11, 12, 13, 14, 2_000_000, 2_000_000);
        var payment = Payment.Create(15, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("BANK-REF");
        var balance = SellerBalance.Create(16, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();

        lifecycle.OnPaymentSucceeded(order, payment, balance, 17);

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnPaymentSucceeded(order, payment, balance, 18));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(2_000_000, balance.PendingIRR);
    }

    [Fact]
    public void DeliveredOrder_BlocksSellerShareUntilComplaintWindowEnds()
    {
        var order=Order.Create(1,10,20,30,1_000_000,1_000_000);
        var payment=Payment.Create(2,1,10,1_000_000);payment.Succeed("REF");
        var balance=SellerBalance.Create(3,20);
        var lifecycle=new OrderFinancialLifecycle();
        lifecycle.OnPaymentSucceeded(order,payment,balance,4);
        order.MarkReady();
        var delivery=DeliveryEntity.Create(5,1,20,DateTime.UtcNow.AddHours(1));delivery.MarkReady();
        var delivered=DateTime.UtcNow;
        delivery.ConfirmDelivered("DEL-1",delivered);
        lifecycle.OnDelivered(order,delivery,balance,delivered,delivered.AddDays(2));

        Assert.Equal(OrderStatus.Delivered,order.Status);
        Assert.Equal(1_000_000,balance.BlockedIRR);
        Assert.Equal(0,balance.AvailableIRR);
    }

    [Fact]
    public void SellerWonComplaint_ReleasesSellerHold()
    {
        var order=Order.Create(1,10,20,30,1_000_000,1_000_000);
        var payment=Payment.Create(2,1,10,1_000_000);payment.Succeed("REF");
        var balance=SellerBalance.Create(3,20);var lifecycle=new OrderFinancialLifecycle();
        var hold=lifecycle.OnPaymentSucceeded(order,payment,balance,4);
        order.MarkReady();
        var delivery=DeliveryEntity.Create(5,1,20,DateTime.UtcNow.AddHours(1));delivery.MarkReady();
        var delivered=DateTime.UtcNow.AddDays(-2);delivery.ConfirmDelivered("DEL",delivered);
        lifecycle.OnDelivered(order,delivery,balance,delivered,DateTime.UtcNow.AddDays(-1));
        var complaint=Complaint.Create(6,1,10,20,"Damaged");
        complaint.StartReview();complaint.ResolveForSeller("Evidence accepted");
        lifecycle.OnSellerWon(complaint,order,balance,hold);

        Assert.Equal(OrderStatus.Completed,order.Status);
        Assert.Equal(1_000_000,balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Released,hold.Status);
    }
    [Fact]
    public void DeliveryExpired_RequestsRefundAndRemovesPendingSellerFundsExactlyOnce()
    {
        var order = Order.Create(21, 22, 23, 24, 3_000_000, 3_000_000);
        var payment = Payment.Create(25, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("REF-EXP");
        var balance = SellerBalance.Create(26, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        lifecycle.OnPaymentSucceeded(order, payment, balance, 27);

        order.MarkReady();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        order.SetDeliveryExpiry(expiresAt);
        var delivery = DeliveryEntity.Create(28, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var expiredAt = expiresAt.AddSeconds(1);
        delivery.Expire(expiredAt);

        lifecycle.OnDeliveryExpired(order, delivery, balance, expiredAt);

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnDeliveryExpired(order, delivery, balance, expiredAt.AddSeconds(1)));
        Assert.Equal(0, balance.PendingIRR);
    }

    [Fact]
    public void LateSuccessfulGatewayPayment_CanBeMarkedForManualReconciliation()
    {
        var payment = Payment.Create(31, 32, 33, 500_000);
        payment.Redirect("TestBank", "AUTH-31");
        payment.Cancel();

        payment.RequireReconciliation("BANK-31");

        Assert.Equal(PaymentStatus.ReconciliationRequired, payment.Status);
        Assert.Equal("BANK-31", payment.ReferenceNumber);
    }

    [Fact]
    public void RefundedPayment_CannotBeMovedBackToReconciliation()
    {
        var payment = Payment.Create(41, 42, 43, 500_000);
        payment.Redirect("TestBank", "AUTH-41");
        payment.Succeed("BANK-41");
        payment.MarkRefunded();

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            payment.RequireReconciliation("BANK-42"));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

}