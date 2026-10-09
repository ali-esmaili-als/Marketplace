using Marketplace.Domain.Finance;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class BalanceTransactionTests
{
    [Fact]
    public void Create_PreservesExactRefundIdentity()
    {
        var transaction = BalanceTransaction.Create(
            id: 10,
            sellerId: 20,
            orderId: 30,
            settlementId: null,
            type: BalanceTransactionType.Refund,
            amountIrr: 100,
            beforeIrr: 500,
            afterIrr: 400,
            reference: "REFUND",
            bucket: BalanceBucket.Blocked,
            refundId: 40);

        Assert.Equal(40, transaction.RefundId);
        Assert.Equal(30, transaction.OrderId);
        Assert.Equal(BalanceTransactionType.Refund, transaction.Type);
    }

    [Fact]
    public void Create_LeavesRefundIdentityNullForNonRefundPostingsByDefault()
    {
        var transaction = BalanceTransaction.Create(
            id: 11,
            sellerId: 20,
            orderId: 30,
            settlementId: null,
            type: BalanceTransactionType.Sale,
            amountIrr: 100,
            beforeIrr: 0,
            afterIrr: 100,
            reference: "PAYMENT");

        Assert.Null(transaction.RefundId);
    }
}
