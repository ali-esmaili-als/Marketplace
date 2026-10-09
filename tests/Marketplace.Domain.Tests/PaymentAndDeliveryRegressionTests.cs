using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Delivery;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentAndDeliveryRegressionTests
{
    [Fact]
    public void Payment_CannotSucceedTwice()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        payment.Redirect("TestBank", "AUTH-1");
        payment.Succeed("REF-1");

        Assert.Throws<DomainException>(() => payment.Succeed("REF-2"));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("REF-1", payment.ReferenceNumber);
    }

    [Fact]
    public void FailedPayment_CannotLaterBecomeSuccessfulWithoutReconciliation()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        payment.Redirect("TestBank", "AUTH-1");
        payment.Fail();

        Assert.Throws<DomainException>(() => payment.Succeed("LATE-REF"));
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void LateGatewaySuccess_CanBeMarkedForManualReconciliation()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        payment.Redirect("TestBank", "AUTH-1");
        payment.Fail();

        payment.RequireReconciliation(" BANK-REF ");

        Assert.Equal(PaymentStatus.ReconciliationRequired, payment.Status);
        Assert.Equal("BANK-REF", payment.ReferenceNumber);
    }

    [Fact]
    public void RefundedPayment_CannotBeMovedBackToReconciliation()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        payment.Redirect("TestBank", "AUTH-1");
        payment.Succeed("REF-1");
        payment.MarkRefunded();

        Assert.Throws<DomainException>(() => payment.RequireReconciliation("LATE-REF"));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void PaymentTransaction_CannotBeSucceededAfterFailure()
    {
        var transaction = PaymentTransaction.Create(1, 2, 100_000, "TestBank", "AUTH-1");
        transaction.Fail();

        Assert.Throws<DomainException>(() => transaction.Succeed("LATE-REF"));
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
    }

    [Fact]
    public void DeliveryCode_IsSingleUseAndWrongCodesIncrementAttempts()
    {
        var code = DeliveryCode.Create(1, 2, "123456", DateTime.UtcNow.AddHours(1));
        var now = DateTime.UtcNow;

        Assert.False(code.Verify("000000", now));
        Assert.Equal(1, code.FailedAttempts);
        Assert.True(code.Verify("123456", now.AddSeconds(1)));
        Assert.False(code.Verify("123456", now.AddSeconds(2)));
        Assert.Equal(now.AddSeconds(1), code.UsedAtUtc);
    }

    [Fact]
    public void DeliveryCode_CannotBeUsedAfterExpiry()
    {
        var code = DeliveryCode.Create(1, 2, "123456", DateTime.UtcNow.AddMinutes(1));

        Assert.False(code.Verify("123456", DateTime.UtcNow.AddMinutes(2)));
        Assert.Null(code.UsedAtUtc);
    }

    [Fact]
    public void ExpiredDelivery_RemovesPendingSellerFundsAndRequestsRefund()
    {
        var order = Order.Create(1, 2, 3, 4, 500_000, 500_000);
        order.MarkPaid();
        var expiresAt = DateTime.UtcNow.AddHours(1);
        order.SetDeliveryExpiry(expiresAt);
        order.MarkReady();

        var delivery = DeliveryEntity.Create(5, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var balance = SellerBalance.Create(6, order.SellerId);
        balance.AddPending(order.SellerAmountIRR);
        var lifecycle = new OrderFinancialLifecycle();

        delivery.Expire(expiresAt.AddSeconds(1));
        lifecycle.OnDeliveryExpired(order, delivery, balance, expiresAt.AddSeconds(1));

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(0, balance.AvailableIRR);
    }

    [Fact]
    public void DeliveryCannotBeConfirmedAfterExpiry()
    {
        var delivery = DeliveryEntity.Create(1, 2, 3, DateTime.UtcNow.AddMinutes(1));
        delivery.MarkReady();

        Assert.Throws<DomainException>(() =>
            delivery.ConfirmDelivered("CONFIRM-1", delivery.ExpiresAtUtc.AddSeconds(1)));
        Assert.Equal(DeliveryStatus.Ready, delivery.Status);
    }
}
