using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Common;
using DeliveryEntity = Marketplace.Domain.Delivery.Delivery;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

/// <summary>
/// Application-service regression tests using real domain objects and mocked repositories.
/// The unit of work executes delegates in memory; these are not SQL Server integration tests.
/// </summary>
public sealed class OrderLifecycleServiceTests
{
    [Fact]
    public async Task Successful_payment_callback_creates_hold_delivery_and_ledger_entry()
    {
        var order = Order.Create(11, 12, 13, 14, 2_000_000, 2_000_000);
        var payment = Payment.Create(15, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "authority-1");
        var transaction = PaymentTransaction.Create(17, payment.Id, payment.AmountIRR, "TestBank", "authority-1");
        var balance = SellerBalance.Create(16, order.SellerId);
        var inventory = InventoryItem.Create(18, 19, 10);
        inventory.Reserve(2);
        var reservation = InventoryReservation.Create(20, inventory.ProductVariantId, order.Id, 2, DateTime.UtcNow.AddMinutes(10));

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryEntity?)null);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation> { reservation });
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
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal(2_000_000, balance.PendingIRR);
        Assert.True(reservation.ExpiresAtUtc > DateTime.UtcNow.AddDays(2));
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Once);
        lifecycle.Verify(x => x.AddDelivery(It.IsAny<DeliveryEntity>()), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Bucket == BalanceBucket.Pending && t.AmountIRR == 2_000_000)), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Repeated_successful_callback_does_not_duplicate_financial_effects()
    {
        var order = Order.Create(11, 12, 13, 14, 2_000_000, 2_000_000);
        var payment = Payment.Create(15, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "authority-1");
        var balance = SellerBalance.Create(16, order.SellerId);
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryEntity?)null);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 200L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            unitOfWork.Object, ids.Object, notifications.Object);

        await service.PaymentSucceededAsync(order.Id, "BANK-REF");
        await service.PaymentSucceededAsync(order.Id, "BANK-REF");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(2_000_000, balance.PendingIRR);
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Once);
        lifecycle.Verify(x => x.AddDelivery(It.IsAny<DeliveryEntity>()), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Expiration_releases_inventory_and_fails_payment_and_transaction_after_deadline()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var order = Order.Create(10, 20, 30, 40, 12_000, 12_000);
        var payment = Payment.Create(50, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "authority-1");
        var transaction = PaymentTransaction.Create(51, payment.Id, payment.AmountIRR, "TestBank", "authority-1");
        var inventory = InventoryItem.Create(60, 70, 10);
        inventory.Reserve(2);
        var reservation = InventoryReservation.Create(80, inventory.ProductVariantId, order.Id, 2, expiresAt);
        var orders = new Mock<IOrderRepository>();
        var payments = new Mock<IPaymentRepository>();
        var lifecycle = new Mock<ILifecycleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation> { reservation });
        lifecycle.Setup(x => x.GetInventoryItemAsync(inventory.ProductVariantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);

        var expired = await service.ExpirePendingPaymentAsync(order.Id, expiresAt.AddTicks(1));

        Assert.True(expired);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        Assert.Equal(10, inventory.AvailableQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Expiration_does_not_release_inventory_before_reservation_deadline()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var order = Order.Create(10, 20, 30, 40, 12_000, 12_000);
        var payment = Payment.Create(50, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "authority-1");
        var inventory = InventoryItem.Create(60, 70, 10);
        inventory.Reserve(2);
        var reservation = InventoryReservation.Create(80, inventory.ProductVariantId, order.Id, 2, expiresAt);
        var orders = new Mock<IOrderRepository>();
        var payments = new Mock<IPaymentRepository>();
        var lifecycle = new Mock<ILifecycleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation> { reservation });
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);

        var expired = await service.ExpirePendingPaymentAsync(order.Id, expiresAt.AddTicks(-1));

        Assert.False(expired);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(PaymentStatus.Redirected, payment.Status);
        Assert.Equal(2, inventory.ReservedQuantity);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Expiration_does_not_cancel_an_order_that_is_already_paid()
    {
        var order = Order.Create(10, 20, 30, 40, 12_000, 12_000);
        order.MarkPaid();
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        var lifecycle = new Mock<ILifecycleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);

        var expired = await service.ExpirePendingPaymentAsync(order.Id, DateTime.UtcNow.AddDays(10));

        Assert.False(expired);
        Assert.Equal(OrderStatus.Paid, order.Status);
        payments.Verify(x => x.GetByOrderAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Payment_success_is_rejected_before_mutation_when_seller_balance_is_missing()
    {
        var order = Order.Create(10, 20, 30, 40, 12_000, 12_000);
        var payment = Payment.Create(50, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "authority-1");
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SellerBalance?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.PaymentSucceededAsync(order.Id, "BANK-REF"));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(PaymentStatus.Redirected, payment.Status);
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirmed_delivery_consumes_reserved_inventory_and_moves_seller_funds_to_complaint_hold()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(301, 302, 303, 304, 2_000_000, 2_000_000);
        order.MarkPaid(now.AddMinutes(-10));
        var expiresAt = now.AddHours(2);
        order.SetDeliveryExpiry(expiresAt);
        order.MarkReady();

        var delivery = DeliveryEntity.Create(305, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var code = Marketplace.Domain.Delivery.DeliveryCode.Create(306, order.Id, "123456", expiresAt);

        var balance = SellerBalance.Create(307, order.SellerId);
        balance.AddPending(order.SellerAmountIRR);
        var inventory = InventoryItem.Create(308, 309, 10);
        inventory.Reserve(2);
        var reservation = InventoryReservation.Create(310, inventory.ProductVariantId, order.Id, 2, expiresAt.AddMinutes(1));

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetDeliveryCodeByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation> { reservation });
        lifecycle.Setup(x => x.GetInventoryItemAsync(inventory.ProductVariantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var nextId = 400L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, ids.Object, notifications.Object);

        await service.MarkDeliveredAsync(order.Id, "123456", " CUSTOMER-CONFIRM ", now, now.AddDays(2));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Marketplace.Domain.Delivery.DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(now, code.UsedAtUtc);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(8, inventory.AvailableQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Bucket == BalanceBucket.Pending && t.AmountIRR == order.SellerAmountIRR)), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Bucket == BalanceBucket.Blocked && t.AmountIRR == order.SellerAmountIRR)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task Invalid_delivery_code_persists_failed_attempt_without_changing_financial_state()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(501, 502, 503, 504, 700_000, 700_000);
        order.MarkPaid(now.AddMinutes(-5));
        var expiresAt = now.AddHours(1);
        order.SetDeliveryExpiry(expiresAt);
        order.MarkReady();
        var delivery = DeliveryEntity.Create(505, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var code = Marketplace.Domain.Delivery.DeliveryCode.Create(506, order.Id, "123456", expiresAt);
        var balance = SellerBalance.Create(507, order.SellerId);
        balance.AddPending(order.SellerAmountIRR);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetDeliveryCodeByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.MarkDeliveredAsync(order.Id, "000000", "CUSTOMER-CONFIRM", now, now.AddDays(1)));

        Assert.Equal(1, code.FailedAttempts);
        Assert.Null(code.UsedAtUtc);
        Assert.Equal(OrderStatus.ReadyForDelivery, order.Status);
        Assert.Equal(Marketplace.Domain.Delivery.DeliveryStatus.Ready, delivery.Status);
        Assert.Equal(order.SellerAmountIRR, balance.PendingIRR);
        Assert.Equal(0, balance.BlockedIRR);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delivery_expiry_releases_active_inventory_and_removes_pending_seller_funds_once()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(601, 602, 603, 604, 900_000, 900_000);
        order.MarkPaid(now.AddDays(-4));
        var expiresAt = now.AddMinutes(-1);
        order.SetDeliveryExpiry(expiresAt);
        order.MarkReady();
        var delivery = DeliveryEntity.Create(605, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var balance = SellerBalance.Create(606, order.SellerId);
        balance.AddPending(order.SellerAmountIRR);
        var inventory = InventoryItem.Create(607, 608, 10);
        inventory.Reserve(3);
        var reservation = InventoryReservation.Create(609, inventory.ProductVariantId, order.Id, 3, expiresAt.AddMinutes(1));

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetReservationsByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryReservation> { reservation });
        lifecycle.Setup(x => x.GetInventoryItemAsync(inventory.ProductVariantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inventory);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 700L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, new Mock<INotificationRepository>().Object);

        await service.ExpireDeliveryAsync(order.Id, now);

        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(Marketplace.Domain.Delivery.DeliveryStatus.Expired, delivery.Status);
        Assert.Equal(0, balance.PendingIRR);
        Assert.Equal(0, balance.AvailableIRR);
        Assert.Equal(10, inventory.AvailableQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Bucket == BalanceBucket.Pending &&
                t.AmountIRR == order.SellerAmountIRR && t.Type == BalanceTransactionType.PendingRemoved)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

}
