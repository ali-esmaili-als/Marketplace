using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentReconciliationTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Redirected)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    public void RequireReconciliation_RecordsGatewayReferenceFromNonRefundedStates(PaymentStatus status)
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        if (status == PaymentStatus.Redirected)
            payment.Redirect("TestBank", "AUTH-1");
        else if (status == PaymentStatus.Failed)
            payment.Fail();
        else if (status == PaymentStatus.Cancelled)
            payment.Cancel();

        payment.RequireReconciliation("BANK-REF-1");

        Assert.Equal(PaymentStatus.ReconciliationRequired, payment.Status);
        Assert.Equal("BANK-REF-1", payment.ReferenceNumber);
    }

    [Fact]
    public void RequireReconciliation_RequiresGatewayReference()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);

        Assert.Throws<DomainException>(() => payment.RequireReconciliation(" "));
    }

    [Fact]
    public void ReconciliationRequiredPayment_CanBeMarkedRefundedAfterVerifiedCompensation()
    {
        var payment = Payment.Create(1, 2, 3, 100_000);
        payment.RequireReconciliation("BANK-REF-1");

        payment.MarkRefunded();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.NotNull(payment.RefundedAtUtc);
    }
}
