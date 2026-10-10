using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Sellers;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class OrderActorServiceTests
{
    [Fact]
    public async Task Delivery_confirmation_ignores_client_timestamps_and_uses_platform_complaint_window()
    {
        const long customerId = 12;
        const long sellerUserId = 22;
        const long sellerId = 13;
        const long orderId = 11;
        const string deliveryCodeValue = "593184";

        var order = Order.Create(orderId, customerId, sellerId, 14, 200_000, 200_000);
        order.MarkPaid(DateTime.UtcNow.AddMinutes(-5));
        order.MarkReady();

        var deliveryExpiry = DateTime.UtcNow.AddDays(3);
        order.SetDeliveryExpiry(deliveryExpiry);
        var delivery = Marketplace.Domain.Delivery.Delivery.Create(30, orderId, sellerId, deliveryExpiry);
        delivery.MarkReady();
        var deliveryCode = DeliveryCode.Create(31, orderId, deliveryCodeValue, deliveryExpiry);
        var balance = SellerBalance.Create(32, sellerId);
        balance.AddPending(order.SellerAmountIRR);

        var sellers = new Mock<ISellerManagementRepository>();
        sellers.Setup(x => x.GetSellerByUserIdAsync(sellerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Seller.Create(sellerId, sellerUserId));

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        lifecycle.Setup(x => x.GetDeliveryCodeByOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(deliveryCode);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(sellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var nextId = 100L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(x => x.GetUserIdForSellerAsync(sellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var lifecycleService = new OrderLifecycleService(
            orders.Object, new Mock<IPaymentRepository>().Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);
        var actor = new OrderActorService(sellers.Object, orders.Object, lifecycleService, null!);

        var before = DateTime.UtcNow;
        await actor.DeliverAsync(
            sellerUserId,
            orderId,
            deliveryCodeValue,
            "seller-confirmation",
            DateTime.UtcNow.AddYears(-10),
            DateTime.UtcNow.AddYears(10));
        var after = DateTime.UtcNow;

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAtUtc);
        Assert.NotNull(order.ComplaintExpiresAtUtc);
        Assert.InRange(order.DeliveredAtUtc.Value, before, after);
        Assert.Equal(TimeSpan.FromDays(7), order.ComplaintExpiresAtUtc.Value - order.DeliveredAtUtc.Value);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.NotNull(deliveryCode.UsedAtUtc);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
