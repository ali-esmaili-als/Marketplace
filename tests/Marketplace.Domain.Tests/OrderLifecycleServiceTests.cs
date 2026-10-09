using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Lifecycle;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class OrderLifecycleServiceTests
{
    [Fact]
    public async Task RepeatedSuccessfulCallback_DoesNotCreateAnotherSellerCreditOrHold()
    {
        var order = Order.Create(1, 10, 20, 30, 1_000_000, 1_000_000);
        order.MarkPaid();
        var payment = Payment.Create(2, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Succeed("BANK-REF");
        var balance = SellerBalance.Create(3, order.SellerId);
        balance.AddPending(order.SellerAmountIRR);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            unitOfWork.Object, ids.Object, notifications.Object);

        await service.PaymentSucceededAsync(order.Id, "BANK-REF");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(1_000_000, balance.PendingIRR);
        ids.Verify(x => x.NextAsync(It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        lifecycle.Verify(x => x.AddDelivery(It.IsAny<Marketplace.Domain.Delivery.Delivery>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulPayment_CreatesOneHoldDeliveryAndPendingLedgerEntry()
    {
        var order = Order.Create(11, 12, 13, 14, 2_000_000, 2_000_000);
        var payment = Payment.Create(15, order.Id, order.CustomerId, order.TotalAmountIRR);
        var balance = SellerBalance.Create(16, order.SellerId);
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync((PaymentTransaction?)null);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Marketplace.Domain.Delivery.Delivery?)null);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<InventoryReservation>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 100L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            unitOfWork.Object, ids.Object, notifications.Object);

        await service.PaymentSucceededAsync(order.Id, "BANK-NEW");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(2_000_000, balance.PendingIRR);
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Once);
        lifecycle.Verify(x => x.AddDelivery(It.IsAny<Marketplace.Domain.Delivery.Delivery>()), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Bucket == BalanceBucket.Pending && t.AmountIRR == 2_000_000)), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
