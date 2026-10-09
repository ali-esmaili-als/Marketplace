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

    private static PaymentVerificationService CreateService(
        Mock<IPaymentRepository> payments,
        Mock<IPaymentGatewayFactory> factory)
    {
        var orders = new Mock<IOrderRepository>();
        var lifecycleRepository = new Mock<ILifecycleRepository>();
        var notifications = new Mock<INotificationRepository>();
        var ids = new Mock<IIdGenerator>();
        var uow = new Mock<IUnitOfWork>();
        var lifecycle = new OrderLifecycleService(
            orders.Object, payments.Object, lifecycleRepository.Object, uow.Object, ids.Object, notifications.Object);
        return new PaymentVerificationService(payments.Object, orders.Object, factory.Object, uow.Object, lifecycle);
    }
}
