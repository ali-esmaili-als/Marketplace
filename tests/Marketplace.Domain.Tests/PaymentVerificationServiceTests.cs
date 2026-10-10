using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentVerificationServiceTests
{
    [Fact]
    public async Task Verify_RejectsPaymentOwnedByAnotherCustomerBeforeCallingGateway()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        await Assert.ThrowsAsync<DomainException>(() => service.VerifyAsync(999, 10, "AUTH-10"));

        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Verify_AlreadySucceededPaymentIsIdempotentAndDoesNotContactGateway()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        payment.Succeed("BANK-REF-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        var result = await service.VerifyAsync(30, 10, "AUTH-10");

        Assert.True(result.Paid);
        Assert.Equal("BANK-REF-10", result.Reference);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Verify_RejectsAuthorityMismatchWithoutCallingGateway()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-CORRECT");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        await Assert.ThrowsAsync<DomainException>(() => service.VerifyAsync(30, 10, "AUTH-WRONG"));

        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyTestReturn_RejectsNonTestProviderAndAuthorityMismatch()
    {
        var payments = new Mock<IPaymentRepository>();
        var nonTestPayment = Payment.Create(10, 20, 30, 500_000);
        nonTestPayment.Redirect("BankA", "AUTH-10");
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(nonTestPayment);

        var testPayment = Payment.Create(11, 21, 31, 500_000);
        testPayment.Redirect("TestBank", "AUTH-CORRECT");
        payments.Setup(x => x.GetAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(testPayment);

        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        await Assert.ThrowsAsync<DomainException>(() => service.VerifyTestReturnAsync(10, "AUTH-10", true));
        await Assert.ThrowsAsync<DomainException>(() => service.VerifyTestReturnAsync(11, "AUTH-WRONG", true));

        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Verify_RejectsUndefinedNumericProviderBeforeCallingGateway()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        var undefinedProvider = ((int)PaymentProviderCode.TestBank + 100).ToString();
        payment.Redirect(undefinedProvider, "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        await Assert.ThrowsAsync<DomainException>(() => service.VerifyAsync(30, 10, "AUTH-10"));

        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyTestReturn_DefinitiveGatewayRejectionMarksPaymentAndTransactionFailed()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var transaction = PaymentTransaction.Create(11, payment.Id, payment.AmountIRR, "TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(false, null, "Test provider rejected payment."));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetForExistingPaymentAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = CreateService(payments, factory, uow);

        var result = await service.VerifyTestReturnAsync(10, "AUTH-10", success: true);

        Assert.False(result.Paid);
        Assert.Equal("Test provider rejected payment.", result.Error);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyAll();
    }

    [Fact]
    public async Task Verify_ProviderTimeoutKeepsPaymentAndTransactionPendingForSafeRetry()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var transaction = PaymentTransaction.Create(11, payment.Id, payment.AmountIRR, "TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Provider response timed out."));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = CreateService(payments, factory, uow);

        await Assert.ThrowsAsync<TimeoutException>(() => service.VerifyAsync(30, 10, "AUTH-10"));

        Assert.Equal(PaymentStatus.Redirected, payment.Status);
        Assert.Null(payment.ReferenceNumber);
        Assert.Equal(PaymentTransactionStatus.Initiated, transaction.Status);
        payments.Verify(x => x.GetLatestTransactionAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.VerifyNoOtherCalls();
        gateway.Verify(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyAll();
    }


    [Fact]
    public async Task Verify_AmbiguousProviderResponseKeepsPaymentAndTransactionUnchanged()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var transaction = PaymentTransaction.Create(11, payment.Id, payment.AmountIRR, "TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(false, null, "Provider status is still pending.", false));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = CreateService(payments, factory, uow);

        var result = await service.VerifyAsync(30, 10, "AUTH-10");

        Assert.False(result.Paid);
        Assert.True(result.OutcomeUnknown);
        Assert.Equal("Provider status is still pending.", result.Error);
        Assert.Equal(PaymentStatus.Redirected, payment.Status);
        Assert.Null(payment.ReferenceNumber);
        Assert.Equal(PaymentTransactionStatus.Initiated, transaction.Status);
        payments.Verify(x => x.GetLatestTransactionAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.VerifyNoOtherCalls();
        factory.VerifyAll();
    }

    [Fact]
    public async Task Verify_ExplicitRejectionDoesNotReportFailureIfConcurrentCallbackAlreadySucceeded()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(false, null, "Stale rejection response."));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<int>> action, CancellationToken token) =>
            {
                // Simulate a successful callback winning the race before the rejection
                // transaction re-reads and conditionally updates the persisted payment.
                payment.Succeed("BANK-REF-CONCURRENT");
                return await action(token);
            });
        var service = CreateService(payments, factory, uow);

        var result = await service.VerifyAsync(30, 10, "AUTH-10");

        Assert.True(result.Paid);
        Assert.Equal("BANK-REF-CONCURRENT", result.Reference);
        Assert.Null(result.Error);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        factory.VerifyAll();
    }

    [Fact]
    public async Task Verify_ExplicitProviderRejectionMarksPaymentAndTransactionFailed()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var transaction = PaymentTransaction.Create(11, payment.Id, payment.AmountIRR, "TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.SetupGet(x => x.ProviderName).Returns("TestBank");
        gateway.Setup(x => x.VerifyAsync("AUTH-10", 500_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(false, null, "Provider rejected payment."));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = CreateService(payments, factory, uow);

        var result = await service.VerifyAsync(30, 10, "AUTH-10");

        Assert.False(result.Paid);
        Assert.Equal("Provider rejected payment.", result.Error);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_RefundedPaymentIsTerminalAndDoesNotCallGatewayAgain()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        payment.Succeed("BANK-REF-10");
        payment.MarkRefunded();
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        var result = await service.VerifyAsync(30, 10, "AUTH-10");

        Assert.False(result.Paid);
        Assert.Equal("BANK-REF-10", result.Reference);
        Assert.Equal("Payment is no longer payable.", result.Error);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyTestReturn_RefundedPaymentIsTerminalAndDoesNotCallGatewayAgain()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        payment.Succeed("BANK-REF-10");
        payment.MarkRefunded();
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var service = CreateService(payments, factory);

        var result = await service.VerifyTestReturnAsync(10, "AUTH-10", true);

        Assert.False(result.Paid);
        Assert.Equal("BANK-REF-10", result.Reference);
        Assert.Equal("Payment is no longer payable.", result.Error);
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Verify_GatewaySuccessWithPersistenceFailureMarksPaymentForReconciliation()
    {
        var payment = Payment.Create(980, 981, 982, 500_000);
        payment.Redirect("TestBank", "AUTH-980");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-980", payment.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(true, "BANK-980", null));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);

        var uow = new Mock<IUnitOfWork>();
        var transactionCount = 0;
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref transactionCount) == 1)
                    throw new InvalidOperationException("Simulated database failure during payment finalization.");
                return action(token);
            });
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var orders = new Mock<IOrderRepository>();
        var lifecycleRepository = new Mock<ILifecycleRepository>();
        var notifications = new Mock<INotificationRepository>();
        var ids = new Mock<IIdGenerator>();
        var lifecycle = new OrderLifecycleService(
            orders.Object, payments.Object, lifecycleRepository.Object, uow.Object, ids.Object, notifications.Object);
        var service = new PaymentVerificationService(
            payments.Object, orders.Object, factory.Object, uow.Object, lifecycle);

        var result = await service.VerifyAsync(payment.CustomerId, payment.Id, "AUTH-980");

        Assert.False(result.Paid);
        Assert.False(result.OutcomeUnknown);
        Assert.Contains("reconciliation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentStatus.ReconciliationRequired, payment.Status);
        Assert.Equal("BANK-980", payment.ReferenceNumber);
        Assert.Equal(2, transactionCount);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyAll();
    }

    [Fact]
    public async Task VerifyTestReturn_CancellationDoesNotOverrideConcurrentSuccessfulCallback()
    {
        var payment = Payment.Create(10, 20, 30, 500_000);
        payment.Redirect("TestBank", "AUTH-10");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<int>> action, CancellationToken token) =>
            {
                // Simulate a bank callback committing success just before the stale
                // browser cancellation handler re-reads payment state.
                payment.Succeed("BANK-REF-CONCURRENT");
                return await action(token);
            });

        var service = CreateService(payments, factory, uow);

        var result = await service.VerifyTestReturnAsync(10, "AUTH-10", success: false);

        Assert.True(result.Paid);
        Assert.Equal("BANK-REF-CONCURRENT", result.Reference);
        Assert.Null(result.Error);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        factory.VerifyNoOtherCalls();
    }

    private static PaymentVerificationService CreateService(
        Mock<IPaymentRepository> payments,
        Mock<IPaymentGatewayFactory> factory,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var orders = new Mock<IOrderRepository>();
        var lifecycleRepository = new Mock<ILifecycleRepository>();
        var notifications = new Mock<INotificationRepository>();
        var ids = new Mock<IIdGenerator>();
        var uow = unitOfWork ?? new Mock<IUnitOfWork>();
        var lifecycle = new OrderLifecycleService(
            orders.Object, payments.Object, lifecycleRepository.Object, uow.Object, ids.Object, notifications.Object);
        return new PaymentVerificationService(payments.Object, orders.Object, factory.Object, uow.Object, lifecycle);
    }

    [Fact]
    public async Task Verify_gateway_success_for_cancelled_order_requires_reconciliation_without_creating_seller_funds()
    {
        var order = Marketplace.Domain.Orders.Order.Create(970, 971, 972, 973, 400_000, 400_000);
        order.Cancel();
        var payment = Payment.Create(974, order.Id, order.CustomerId, order.TotalAmountIRR);
        payment.Redirect("TestBank", "AUTH-974");
        var balance = Marketplace.Domain.Finance.SellerBalance.Create(975, order.SellerId);

        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetByOrderAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);
        var lifecycleRepository = new Mock<ILifecycleRepository>();
        lifecycleRepository.Setup(x => x.GetSellerBalanceAsync(order.SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.VerifyAsync("AUTH-974", 400_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(true, "BANK-974", null));
        var factory = new Mock<IPaymentGatewayFactory>(MockBehavior.Strict);
        factory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var lifecycle = new OrderLifecycleService(orders.Object, payments.Object, lifecycleRepository.Object,
            uow.Object, new Mock<IIdGenerator>().Object, new Mock<INotificationRepository>().Object);
        var service = new PaymentVerificationService(payments.Object, orders.Object, factory.Object, uow.Object, lifecycle);

        var result = await service.VerifyAsync(order.CustomerId, payment.Id, "AUTH-974");

        Assert.False(result.Paid);
        Assert.False(result.OutcomeUnknown);
        Assert.Contains("reconciliation", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Marketplace.Domain.Orders.OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.ReconciliationRequired, payment.Status);
        Assert.Equal("BANK-974", payment.ReferenceNumber);
        Assert.Equal(0, balance.PendingIRR);
        lifecycleRepository.Verify(x => x.AddBalanceHold(It.IsAny<Marketplace.Domain.Finance.SellerBalanceHold>()), Times.Never);
        lifecycleRepository.Verify(x => x.AddDelivery(It.IsAny<Marketplace.Domain.Delivery.Delivery>()), Times.Never);
        lifecycleRepository.Verify(x => x.AddBalanceTransaction(It.IsAny<Marketplace.Domain.Finance.BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        factory.VerifyAll();
    }

}
