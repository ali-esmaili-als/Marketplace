using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class RefundSafetyTests
{
    [Fact]
    public void Refund_CannotBeStartedTwiceAfterEnteringProcessing()
    {
        var refund = Refund.Create(1, 2, 3, 4, 50_000, RefundReason.Other);
        refund.Approve();
        refund.StartProcessing();

        Assert.Throws<DomainException>(() => refund.StartProcessing());
    }

    [Fact]
    public void Refund_CannotBeCompletedWithoutProcessing()
    {
        var refund = Refund.Create(1, 2, 3, 4, 50_000, RefundReason.Other);

        Assert.Throws<DomainException>(() => refund.Complete("BANK-REF"));
    }

    [Fact]
    public void ProcessingRefund_CannotBeRejectedWhileGatewayOutcomeMayBeUnknown()
    {
        var refund = Refund.Create(1, 2, 3, 4, 50_000, RefundReason.Other);
        refund.Approve();
        refund.StartProcessing();

        Assert.Throws<DomainException>(() => refund.Reject("Operator has not confirmed bank outcome."));
        Assert.Equal(RefundStatus.Processing, refund.Status);
    }

    [Fact]
    public void Refund_CanFailOnlyAfterProcessingHasStarted()
    {
        var refund = Refund.Create(1, 2, 3, 4, 50_000, RefundReason.Other);

        Assert.Throws<DomainException>(() => refund.Fail("Gateway rejected refund."));
        Assert.Equal(RefundStatus.Requested, refund.Status);

        refund.Approve();
        refund.StartProcessing();
        refund.Fail("Gateway rejected refund.");
        Assert.Equal(RefundStatus.Failed, refund.Status);
    }

    [Fact]
    public void OrderRefundRequest_IsRejectedBeforeDelivery()
    {
        var order = Order.Create(1, 2, 3, 4, 100_000, 100_000);

        Assert.Throws<DomainException>(() => order.RequestRefund());
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void DeliveredOrder_CanEnterRefundRequestedState()
    {
        var order = Order.Create(1, 2, 3, 4, 100_000, 100_000);
        order.MarkPaid();
        order.MarkReady();
        var deliveredAt = DateTime.UtcNow;
        order.MarkDelivered(deliveredAt, deliveredAt.AddDays(2));

        order.RequestRefund();

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
    }
}
