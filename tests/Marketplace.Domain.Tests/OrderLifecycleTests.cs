using Marketplace.Domain.Complaints;
using Marketplace.Domain.Delivery;
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
    public void DeliveredOrder_BlocksSellerShareUntilComplaintWindowEnds()
    {
        var order=Order.Create(1,10,20,30,1_000_000,1_000_000);
        var payment=Payment.Create(2,1,10,1_000_000);payment.Succeed("REF");
        var balance=SellerBalance.Create(3,20);
        var lifecycle=new OrderFinancialLifecycle();
        lifecycle.OnPaymentSucceeded(order,payment,balance,4);
        var delivery=Delivery.Create(5,1,20,DateTime.UtcNow.AddHours(1));delivery.MarkReady();
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
        var delivery=Delivery.Create(5,1,20,DateTime.UtcNow.AddHours(1));delivery.MarkReady();
        var delivered=DateTime.UtcNow.AddDays(-2);delivery.ConfirmDelivered("DEL",delivered);
        lifecycle.OnDelivered(order,delivery,balance,delivered,DateTime.UtcNow.AddDays(-1));
        var complaint=Complaint.Create(6,1,10,20,"Damaged");
        complaint.StartReview();complaint.ResolveForSeller("Evidence accepted");
        lifecycle.OnSellerWon(complaint,order,balance,hold);

        Assert.Equal(OrderStatus.Completed,order.Status);
        Assert.Equal(1_000_000,balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Released,hold.Status);
    }
}