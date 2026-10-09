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

/// <summary>
/// Security and idempotency regression tests for payment verification.
/// Gateways and persistence are mocked; these are application unit tests, not provider integration tests.
/// </summary>
public sealed class PaymentVerificationSecurityTests
{
    [Fact]
    public async Task Verify_rejects_a_payment_owned_by_another_customer_before_calling_gateway()
    {
        var payment = CreateRedirectedPayment();
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var orders = new Mock<IOrderRepository>();
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        var uow = new Mock<IUnitOfWork>();
        var lifecycle = CreateLifecycle(orders.Object, payments.Object, uow.Object);

        var service = new PaymentVerificationService(
            payments.Object, orders.Object, gatewayFactory.Object, uow.Object, lifecycle);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.VerifyAsync(userId: 999, paymentId: payment.Id, authority: "authority-1"));

        gatewayFactory.Verify(x => x.GetAsync(It.IsAny<PaymentProviderCode>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_rejects_an_authority_mismatch_before_calling_gateway()
    {
        var payment = CreateRedirectedPayment();
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var orders = new Mock<IOrderRepository>();
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        var uow = new Mock<IUnitOfWork>();
        var lifecycle = CreateLifecycle(orders.Object, payments.Object, uow.Object);

        var service = new PaymentVerificationService(
            payments.Object, orders.Object, gatewayFactory.Object, uow.Object, lifecycle);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.VerifyAsync(userId: payment.CustomerId, paymentId: payment.Id, authority: "wrong-authority"));

        gatewayFactory.Verify(x => x.GetAsync(It.IsAny<PaymentProviderCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_returns_existing_success_without_reverifying_gateway()
    {
        var payment = CreateRedirectedPayment();
        payment.Succeed("bank-reference-1");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var orders = new Mock<IOrderRepository>();
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        var uow = new Mock<IUnitOfWork>();
        var lifecycle = Mock.Of<OrderLifecycleService>();

        var service = new PaymentVerificationService(
            payments.Object, orders.Object, gatewayFactory.Object, uow.Object, lifecycle);

        var result = await service.VerifyAsync(payment.CustomerId, payment.Id, "authority-1");

        Assert.True(result.Paid);
        Assert.Equal("bank-reference-1", result.Reference);
        Assert.Null(result.Error);
        gatewayFactory.Verify(x => x.GetAsync(It.IsAny<PaymentProviderCode>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Test_return_rejects_a_non_test_provider_even_when_authority_matches()
    {
        var payment = CreateRedirectedPayment(provider: "ProductionBank");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        var orders = new Mock<IOrderRepository>();
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        var uow = new Mock<IUnitOfWork>();
        var lifecycle = Mock.Of<OrderLifecycleService>();

        var service = new PaymentVerificationService(
            payments.Object, orders.Object, gatewayFactory.Object, uow.Object, lifecycle);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.VerifyTestReturnAsync(payment.Id, "authority-1", success: true));

        gatewayFactory.Verify(x => x.GetForExistingPaymentAsync(
            PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Failed_gateway_verification_fails_payment_and_initiated_transaction_together()
    {
        var payment = CreateRedirectedPayment();
        var transaction = PaymentTransaction.Create(21, payment.Id, payment.AmountIRR, "TestBank", "authority-1");
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        payments.Setup(x => x.GetLatestTransactionAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var orders = new Mock<IOrderRepository>();
        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupGet(x => x.ProviderName).Returns("TestBank");
        gateway.Setup(x => x.VerifyAsync("authority-1", payment.AmountIRR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentVerification(false, null, "Bank rejected verification."));
        var gatewayFactory = new Mock<IPaymentGatewayFactory>();
        gatewayFactory.Setup(x => x.GetAsync(PaymentProviderCode.TestBank, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateway.Object);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var lifecycle = Mock.Of<OrderLifecycleService>();

        var service = new PaymentVerificationService(
            payments.Object, orders.Object, gatewayFactory.Object, uow.Object, lifecycle);

        var result = await service.VerifyAsync(payment.CustomerId, payment.Id, "authority-1");

        Assert.False(result.Paid);
        Assert.Equal("Bank rejected verification.", result.Error);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OrderLifecycleService CreateLifecycle(
        IOrderRepository orders, IPaymentRepository payments, IUnitOfWork uow)
        => new(
            orders,
            payments,
            Mock.Of<ILifecycleRepository>(),
            uow,
            Mock.Of<IIdGenerator>(),
            Mock.Of<INotificationRepository>());

    private static Payment CreateRedirectedPayment(string provider = "TestBank")
    {
        var payment = Payment.Create(10, 11, 12, 130_000);
        payment.Redirect(provider, "authority-1");
        return payment;
    }
}
