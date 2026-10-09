using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

/// <summary>
/// Regression tests for the payment/order/delivery state machine.
/// These exercise real domain aggregates; they do not replace database/provider integration tests.
/// </summary>
public sealed class PaymentOrderLifecycleTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(2);

    [Fact]
    public void Successful_payment_can_transition_order_to_delivery_and_completion()
    {
        var order = Order.Create(10, 20, 30, 40, 12_000, 10_000);
        var payment = Payment.Create(50, order.Id, order.CustomerId, order.TotalAmountIRR);
        var delivery = Delivery.Create(60, order.Id, order.SellerId, Future);

        payment.Redirect("TestBank", "authority-1");
        payment.Succeed("reference-1");
        order.MarkPaid(payment.PaidAtUtc);
        delivery.MarkReady();
        order.MarkReady();

        var deliveredAt = DateTime.UtcNow;
        var complaintExpiresAt = deliveredAt.AddDays(3);
        delivery.ConfirmDelivered("customer-confirmation-1", deliveredAt);
        order.MarkDelivered(deliveredAt, complaintExpiresAt);
        order.Complete(complaintExpiresAt.AddTicks(1));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal("reference-1", payment.ReferenceNumber);
    }

    [Fact]
    public void Duplicate_success_callback_is_rejected_by_payment_aggregate()
    {
        var payment = Payment.Create(1, 2, 3, 1000);
        payment.Redirect("TestBank", "authority-1");
        payment.Succeed("reference-1");

        Assert.Throws<DomainException>(() => payment.Succeed("reference-1"));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("reference-1", payment.ReferenceNumber);
    }

    [Fact]
    public void Failed_payment_cannot_be_resurrected_by_a_late_success_callback()
    {
        var payment = Payment.Create(1, 2, 3, 1000);
        payment.Redirect("TestBank", "authority-1");
        payment.Fail();

        Assert.Throws<DomainException>(() => payment.Succeed("late-reference"));
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void Cancelled_payment_cannot_be_resurrected_by_a_late_success_callback()
    {
        var payment = Payment.Create(1, 2, 3, 1000);
        payment.Cancel();

        Assert.Throws<DomainException>(() => payment.Succeed("late-reference"));
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void Cancelled_order_cannot_be_marked_paid_after_expiration_race()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1000);
        order.Cancel();

        Assert.Throws<DomainException>(() => order.MarkPaid());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Duplicate_delivery_confirmation_is_rejected()
    {
        var delivery = Delivery.Create(1, 2, 3, Future);
        delivery.MarkReady();
        delivery.ConfirmDelivered("proof-1", DateTime.UtcNow);

        Assert.Throws<DomainException>(() => delivery.ConfirmDelivered("proof-2", DateTime.UtcNow));
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal("proof-1", delivery.ConfirmationReference);
    }

    [Fact]
    public void Delivery_confirmation_after_expiry_is_rejected()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(1);
        var delivery = Delivery.Create(1, 2, 3, expiresAt);
        delivery.MarkReady();

        Assert.Throws<DomainException>(() => delivery.ConfirmDelivered("proof", expiresAt.AddTicks(1)));
        Assert.Equal(DeliveryStatus.Ready, delivery.Status);
    }

    [Fact]
    public void Order_cannot_complete_before_complaint_window_ends()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1000);
        order.MarkPaid();
        order.MarkReady();
        var deliveredAt = DateTime.UtcNow;
        var complaintExpiresAt = deliveredAt.AddDays(3);
        order.MarkDelivered(deliveredAt, complaintExpiresAt);

        Assert.Throws<DomainException>(() => order.Complete(complaintExpiresAt.AddTicks(-1)));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void Delivery_expiration_requires_expiry_timestamp_to_have_passed()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1000);
        order.MarkPaid();
        order.SetDeliveryExpiry(Future);
        order.MarkReady();

        Assert.Throws<DomainException>(() => order.MarkDeliveryExpired(Future.AddTicks(-1)));
        order.MarkDeliveryExpired(Future);

        Assert.Equal(OrderStatus.DeliveryExpired, order.Status);
    }

    [Fact]
    public void Refunded_payment_cannot_be_marked_failed()
    {
        var payment = Payment.Create(1, 2, 3, 1000);
        payment.Succeed("reference");
        payment.MarkRefunded();

        Assert.Throws<DomainException>(() => payment.Fail());
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }
}
