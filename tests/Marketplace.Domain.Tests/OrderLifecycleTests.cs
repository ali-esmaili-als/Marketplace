using System;
using Marketplace.Domain.Complaints;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
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
    public void SuccessfulPayment_WithWrongAmountDoesNotMutateOrderOrBalance()
    {
        var order = Order.Create(101, 102, 103, 104, 1_000_000, 1_000_000);
        var payment = Payment.Create(105, order.Id, order.CustomerId, 999_999);
        payment.Succeed("WRONG-AMOUNT");
        var balance = SellerBalance.Create(106, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnPaymentSucceeded(order, payment, balance, 107));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(0, balance.PendingIRR);
    }

    [Fact]
    public void SuccessfulPayment_ForAnotherCustomerDoesNotMutateOrderOrBalance()
    {
        var order = Order.Create(111, 112, 113, 114, 1_000_000, 1_000_000);
        var payment = Payment.Create(115, order.Id, 999, order.TotalAmountIRR);
        payment.Succeed("WRONG-CUSTOMER");
        var balance = SellerBalance.Create(116, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnPaymentSucceeded(order, payment, balance, 117));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(0, balance.PendingIRR);
    }

    [Fact]
    public void SuccessfulPayment_WithAnotherSellersBalanceDoesNotMutateAggregates()
    {
        var order = Order.Create(121, 122, 123, 124, 1_000_000, 1_000_000);
        var payment = Payment.Create(125, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("WRONG-BALANCE");
        var balance = SellerBalance.Create(126, 999);
        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnPaymentSucceeded(order, payment, balance, 127));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(0, balance.PendingIRR);
    }

    [Fact]
    public void ZeroSellerShareDoesNotMarkOrderPaidBeforeRejecting()
    {
        var order = Order.Create(131, 132, 133, 134, 1_000_000, 1_000_000);
        order.SetSellerAmount(0);
        var payment = Payment.Create(135, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("ZERO-SELLER-SHARE");
        var balance = SellerBalance.Create(136, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnPaymentSucceeded(order, payment, balance, 137));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(0, balance.PendingIRR);
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
    public void DeliveredOrder_WithInsufficientPendingFundsDoesNotMutateOrder()
    {
        var order = Order.Create(141, 142, 143, 144, 1_000_000, 1_000_000);
        var payment = Payment.Create(145, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("DELIVERY-TEST");
        var balance = SellerBalance.Create(146, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        lifecycle.OnPaymentSucceeded(order, payment, balance, 147);
        order.MarkReady();
        var deliveredAt = DateTime.UtcNow;
        var delivery = DeliveryEntity.Create(148, order.Id, order.SellerId, deliveredAt.AddDays(1));
        delivery.MarkReady();
        delivery.ConfirmDelivered("DELIVERY-141", deliveredAt);
        balance.RemovePending(order.SellerAmountIRR);

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnDelivered(order, delivery, balance, deliveredAt, deliveredAt.AddDays(2)));

        Assert.Equal(OrderStatus.ReadyForDelivery, order.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(0, balance.BlockedIRR);
    }

    [Fact]
    public void DeliveryExpiry_WithInsufficientPendingFundsDoesNotChangeOrderStatus()
    {
        var order = Order.Create(151, 152, 153, 154, 1_000_000, 1_000_000);
        var payment = Payment.Create(155, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("EXPIRY-TEST");
        var balance = SellerBalance.Create(156, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        lifecycle.OnPaymentSucceeded(order, payment, balance, 157);
        order.MarkReady();
        var expiresAt = DateTime.UtcNow.AddMinutes(10);
        order.SetDeliveryExpiry(expiresAt);
        var delivery = DeliveryEntity.Create(158, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        delivery.Expire(expiresAt.AddSeconds(1));
        balance.RemovePending(order.SellerAmountIRR);

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
            lifecycle.OnDeliveryExpired(order, delivery, balance, expiresAt.AddSeconds(1)));

        Assert.Equal(OrderStatus.ReadyForDelivery, order.Status);
        Assert.Equal(0, balance.PendingIRR);
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
    public void CompletedRefundAfterDeliveryExpiry_RemovesPendingFundsAndConsumesHold()
    {
        var order = Order.Create(51, 52, 53, 54, 1_200_000, 1_200_000);
        var payment = Payment.Create(55, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("REF-51");
        var balance = SellerBalance.Create(56, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 57);

        order.MarkReady();
        var expires = DateTime.UtcNow.AddMinutes(10);
        order.SetDeliveryExpiry(expires);
        var delivery = DeliveryEntity.Create(58, order.Id, order.SellerId, expires);
        delivery.MarkReady();
        delivery.Expire(expires.AddSeconds(1));
        lifecycle.OnDeliveryExpired(order, delivery, balance, expires.AddSeconds(1));

        var refund = Refund.Create(59, order.Id, payment.Id, order.CustomerId, order.TotalAmountIRR, RefundReason.DeliveryExpired);
        refund.StartProcessing();
        refund.Complete("REFUND-51");
        lifecycle.CompleteRefund(order, payment, refund, balance, hold);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(BalanceHoldStatus.Consumed, hold.Status);
    }

    [Fact]
    public void CompletedRefundAfterDelivery_ConsumesBlockedFundsWithoutMakingThemWithdrawable()
    {
        var order = Order.Create(61, 62, 63, 64, 2_400_000, 2_400_000);
        var payment = Payment.Create(65, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("REF-61");
        var balance = SellerBalance.Create(66, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 67);

        order.MarkReady();
        var deliveredAt = DateTime.UtcNow;
        var delivery = DeliveryEntity.Create(68, order.Id, order.SellerId, deliveredAt.AddDays(1));
        delivery.MarkReady();
        delivery.ConfirmDelivered("DEL-61", deliveredAt);
        lifecycle.OnDelivered(order, delivery, balance, deliveredAt, deliveredAt.AddDays(3));
        order.RequestRefund();

        var refund = Refund.Create(69, order.Id, payment.Id, order.CustomerId, order.TotalAmountIRR, RefundReason.ComplaintCustomerWon);
        refund.StartProcessing();
        refund.Complete("REFUND-61");
        lifecycle.CompleteRefund(order, payment, refund, balance, hold);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(0, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Consumed, hold.Status);
    }

    [Fact]
    public void CompleteRefund_RejectsIncompleteRefundWithoutChangingFinancialBuckets()
    {
        var order = Order.Create(71, 72, 73, 74, 800_000, 800_000);
        var payment = Payment.Create(75, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("REF-71");
        var balance = SellerBalance.Create(76, order.SellerId);
        var lifecycle = new OrderFinancialLifecycle();
        var hold = lifecycle.OnPaymentSucceeded(order, payment, balance, 77);
        order.MarkReady();
        var expires = DateTime.UtcNow.AddMinutes(15);
        order.SetDeliveryExpiry(expires);
        var delivery = DeliveryEntity.Create(78, order.Id, order.SellerId, expires);
        delivery.MarkReady();
        delivery.Expire(expires.AddSeconds(1));
        lifecycle.OnDeliveryExpired(order, delivery, balance, expires.AddSeconds(1));
        var refund = Refund.Create(79, order.Id, payment.Id, order.CustomerId, order.TotalAmountIRR, RefundReason.DeliveryExpired);

        Assert.Throws<Marketplace.Domain.Common.DomainException>(() =>
        {
            lifecycle.CompleteRefund(order, payment, refund, balance, hold);
        });

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(800_000, hold.AmountIRR);
        Assert.Equal(BalanceHoldStatus.Active, hold.Status);
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