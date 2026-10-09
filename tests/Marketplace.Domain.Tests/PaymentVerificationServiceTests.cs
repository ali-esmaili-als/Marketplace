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
}
