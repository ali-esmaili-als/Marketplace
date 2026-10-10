using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
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
        var nextId = 1000L;
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
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

        await service.MarkDeliveredAsync(order.Id, "123456", " CUSTOMER-CONFIRM ", now.AddYears(-1), now.AddYears(10));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Marketplace.Domain.Delivery.DeliveryStatus.Delivered, delivery.Status);
        Assert.NotNull(code.UsedAtUtc);
        Assert.InRange(code.UsedAtUtc!.Value, now.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(code.UsedAtUtc, delivery.DeliveredAtUtc);
        Assert.Equal(code.UsedAtUtc!.Value.AddDays(7), order.ComplaintExpiresAtUtc);
        Assert.NotEqual(now.AddYears(-1), delivery.DeliveredAtUtc);
        Assert.NotEqual(now.AddYears(10), order.ComplaintExpiresAtUtc);
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
    public async Task Repeated_ready_command_does_not_issue_a_second_delivery_code()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(491, 492, 493, 494, 700_000, 700_000);
        order.MarkPaid(now.AddMinutes(-5));
        order.SetDeliveryExpiry(now.AddDays(2));
        order.MarkReady();
        var delivery = DeliveryEntity.Create(495, order.Id, order.SellerId, now.AddDays(2));
        delivery.MarkReady();
        var code = Marketplace.Domain.Delivery.DeliveryCode.Create(496, order.Id, "123456", now.AddDays(2));

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetDeliveryByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        lifecycle.Setup(x => x.GetDeliveryCodeByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var ids = new Mock<IIdGenerator>();
        var notifications = new Mock<INotificationRepository>();
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, notifications.Object);

        await service.MarkReadyForDeliveryAsync(order.Id);

        Assert.Equal(OrderStatus.ReadyForDelivery, order.Status);
        Assert.Equal(Marketplace.Domain.Delivery.DeliveryStatus.Ready, delivery.Status);
        lifecycle.Verify(x => x.AddDeliveryCode(It.IsAny<Marketplace.Domain.Delivery.DeliveryCode>()), Times.Never);
        notifications.Verify(x => x.Add(It.IsAny<Marketplace.Domain.Notifications.Notification>()), Times.Never);
        ids.Verify(x => x.NextAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        var expiresAt = now.AddMinutes(1);
        order.SetDeliveryExpiry(expiresAt);
        order.MarkReady();
        var delivery = DeliveryEntity.Create(605, order.Id, order.SellerId, expiresAt);
        delivery.MarkReady();
        var expiryCheckTime = expiresAt.AddSeconds(1);
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

        await service.ExpireDeliveryAsync(order.Id, expiryCheckTime);
        // Worker retries after a committed expiry must not duplicate the inventory release or ledger row.
        await service.ExpireDeliveryAsync(order.Id, expiryCheckTime.AddMinutes(1));

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



    [Fact]
    public async Task Customer_won_complaint_requests_refund_without_releasing_seller_hold()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(801, 802, 803, 804, 600_000, 600_000);
        order.MarkPaid(now.AddDays(-3));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-2), now.AddDays(1));
        var balance = SellerBalance.Create(805, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(806, order.SellerId, order.Id, order.SellerAmountIRR, "Secure order hold");
        var complaint = Complaint.Create(807, order.Id, order.CustomerId, order.SellerId, "Item arrived damaged");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetComplaintAsync(complaint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(complaint);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 2000L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, new Mock<INotificationRepository>().Object);

        await service.ResolveComplaintAsync(complaint.Id, true, "Evidence confirms item was damaged");

        Assert.Equal(ComplaintStatus.CustomerWon, complaint.Status);
        Assert.Equal(OrderStatus.RefundRequested, order.Status);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(0, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Active, hold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Seller_won_complaint_releases_blocked_funds_and_records_ledger_once()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(811, 812, 813, 814, 750_000, 750_000);
        order.MarkPaid(now.AddDays(-4));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-3), now.AddDays(-1));
        var balance = SellerBalance.Create(815, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(816, order.SellerId, order.Id, order.SellerAmountIRR, "Secure order hold");
        var complaint = Complaint.Create(817, order.Id, order.CustomerId, order.SellerId, "Customer reported a defect");
        complaint.StartReview();

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetComplaintAsync(complaint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(complaint);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 900L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, new Mock<INotificationRepository>().Object);

        await service.ResolveComplaintAsync(complaint.Id, false, "Evidence confirms delivery matched the listing");

        Assert.Equal(ComplaintStatus.SellerWon, complaint.Status);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(order.SellerAmountIRR, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Released, hold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Blocked && t.AmountIRR == order.SellerAmountIRR &&
                t.BalanceBeforeIRR == order.SellerAmountIRR && t.BalanceAfterIRR == 0)), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Available && t.AmountIRR == order.SellerAmountIRR &&
                t.BalanceBeforeIRR == 0 && t.BalanceAfterIRR == order.SellerAmountIRR)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }



    [Fact]
    public async Task Complaint_cannot_be_opened_by_another_customer()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(821, 822, 823, 824, 100_000, 100_000);
        order.MarkPaid(now.AddMinutes(-5));
        order.MarkReady();
        order.MarkDelivered(now, now.AddHours(2));
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<long>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<long>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() => service.OpenComplaintAsync(order.Id, 999_999, "Not my order"));

        lifecycle.Verify(x => x.AddComplaint(It.IsAny<Complaint>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complaint_cannot_be_opened_twice_for_the_same_order()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(831, 832, 833, 834, 100_000, 100_000);
        order.MarkPaid(now.AddMinutes(-5));
        order.MarkReady();
        order.MarkDelivered(now, now.AddHours(2));
        var existing = Complaint.Create(835, order.Id, order.CustomerId, order.SellerId, "Existing complaint");
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetOpenComplaintByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<long>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<long>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() => service.OpenComplaintAsync(order.Id, order.CustomerId, "Another complaint"));

        lifecycle.Verify(x => x.AddComplaint(It.IsAny<Complaint>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }



    [Fact]
    public async Task Resolving_the_same_complaint_twice_cannot_release_seller_funds_twice()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(841, 842, 843, 844, 250_000, 250_000);
        order.MarkPaid(now.AddDays(-5));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-3), now.AddDays(-1));
        var balance = SellerBalance.Create(845, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(846, order.SellerId, order.Id, order.SellerAmountIRR, "Secure order hold");
        var complaint = Complaint.Create(847, order.Id, order.CustomerId, order.SellerId, "Item matched listing");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetComplaintAsync(complaint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(complaint);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 950L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, new Mock<INotificationRepository>().Object);

        await service.ResolveComplaintAsync(complaint.Id, false, "Evidence supports seller");
        await Assert.ThrowsAsync<DomainException>(() =>
            service.ResolveComplaintAsync(complaint.Id, false, "Duplicate resolution"));

        Assert.Equal(ComplaintStatus.SellerWon, complaint.Status);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(order.SellerAmountIRR, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Released, hold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Blocked)), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Available)), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Exactly(2));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Closing_completed_order_releases_hold_only_once_after_complaint_window()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(861, 862, 863, 864, 350_000, 350_000);
        order.MarkPaid(now.AddDays(-5));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-3), now.AddDays(-1));
        var balance = SellerBalance.Create(865, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(866, order.SellerId, order.Id, order.SellerAmountIRR, "Complaint window hold");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetOpenComplaintByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Complaint?)null);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var nextId = 970L;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Interlocked.Increment(ref nextId)));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, ids.Object, new Mock<INotificationRepository>().Object);

        await service.CloseCompletedOrderAsync(order.Id, now);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(0, balance.BlockedIRR);
        Assert.Equal(order.SellerAmountIRR, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Released, hold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Blocked && t.Reference == "COMPLAINT_WINDOW_CLOSED" &&
                t.AmountIRR == order.SellerAmountIRR && t.BalanceBeforeIRR == order.SellerAmountIRR &&
                t.BalanceAfterIRR == 0)), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(
            It.Is<BalanceTransaction>(t => t.Type == BalanceTransactionType.ComplaintHoldReleased &&
                t.Bucket == BalanceBucket.Available && t.Reference == "COMPLAINT_WINDOW_CLOSED" &&
                t.AmountIRR == order.SellerAmountIRR && t.BalanceBeforeIRR == 0 &&
                t.BalanceAfterIRR == order.SellerAmountIRR)), Times.Once);
        await Assert.ThrowsAsync<DomainException>(() => service.CloseCompletedOrderAsync(order.Id, now));
        Assert.Equal(order.SellerAmountIRR, balance.AvailableIRR);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Exactly(2));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }



    [Fact]
    public async Task Customer_won_resolution_rejects_complaint_with_mismatched_order_parties()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(881, 882, 883, 884, 150_000, 150_000);
        order.MarkPaid(now.AddDays(-2));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-1), now.AddDays(1));
        var balance = SellerBalance.Create(885, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(886, order.SellerId, order.Id, order.SellerAmountIRR, "Secure order hold");
        var complaint = Complaint.Create(887, order.Id, 999_888, order.SellerId, "Complaint party does not match order");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetComplaintAsync(complaint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(complaint);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.ResolveComplaintAsync(complaint.Id, true, "Review attempted"));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(BalanceHoldStatus.Active, hold.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }



    [Fact]
    public async Task Closing_order_with_active_complaint_keeps_seller_hold_blocked()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(901, 902, 903, 904, 420_000, 420_000);
        order.MarkPaid(now.AddDays(-5));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-3), now.AddDays(-1));
        var balance = SellerBalance.Create(905, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var hold = SellerBalanceHold.Create(906, order.SellerId, order.Id, order.SellerAmountIRR, "Complaint review hold");
        var complaint = Complaint.Create(907, order.Id, order.CustomerId, order.SellerId, "Buyer complaint still under review");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetOpenComplaintByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(complaint);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() => service.CloseCompletedOrderAsync(order.Id, now));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(0, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Active, hold.Status);
        lifecycle.Verify(x => x.GetSellerBalanceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.Verify(x => x.GetActiveHoldByOrderAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Closing_order_rejects_mismatched_hold_without_mutating_balance_or_order()
    {
        var now = DateTime.UtcNow;
        var order = Order.Create(911, 912, 913, 914, 520_000, 520_000);
        order.MarkPaid(now.AddDays(-5));
        order.MarkReady();
        order.MarkDelivered(now.AddDays(-3), now.AddDays(-1));
        var balance = SellerBalance.Create(915, order.SellerId);
        balance.AddAvailable(order.SellerAmountIRR);
        balance.Block(order.SellerAmountIRR);
        var wrongOrderHold = SellerBalanceHold.Create(916, order.SellerId, 999_999, order.SellerAmountIRR, "Mismatched hold");

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetOpenComplaintByOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Complaint?)null);
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetActiveHoldByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(wrongOrderHold);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, new Mock<IPaymentRepository>().Object,
            lifecycle.Object, uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() => service.CloseCompletedOrderAsync(order.Id, now));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(order.SellerAmountIRR, balance.BlockedIRR);
        Assert.Equal(0, balance.AvailableIRR);
        Assert.Equal(BalanceHoldStatus.Active, wrongOrderHold.Status);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }


    [Fact]
    public async Task Successful_payment_callback_does_not_silently_ignore_cancelled_order()
    {
        var order = Order.Create(940, 941, 942, 943, 250_000, 250_000);
        order.Cancel();

        var payment = Payment.Create(944, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-944");
        payment.Succeed("BANK-944");
        var balance = SellerBalance.Create(945, order.SellerId);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(balance);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        var service = new OrderLifecycleService(orders.Object, payments.Object, lifecycle.Object,
            uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);

        await Assert.ThrowsAsync<DomainException>(() => service.PaymentSucceededAsync(order.Id, "BANK-944"));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(0, balance.PendingIRR);
        lifecycle.Verify(x => x.AddBalanceHold(It.IsAny<SellerBalanceHold>()), Times.Never);
        lifecycle.Verify(x => x.AddDelivery(It.IsAny<DeliveryEntity>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

}
