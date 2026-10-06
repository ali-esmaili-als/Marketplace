using Marketplace.Domain.Common;

namespace Marketplace.Domain.Payments;

public sealed class Payment : AggregateRoot<long>
{
    private Payment() { }
    public long OrderId { get; private set; }
    public long PaymentAttemptId { get; private set; }
    public long AmountIRR { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal FxRateToIRR { get; private set; }
    public string Gateway { get; private set; } = null!;
    public string GatewayTransactionId { get; private set; } = null!;
    public DateTime PaidAtUtc { get; private set; }

    public static Payment Create(long id,long orderId,long paymentAttemptId,long amountIrr,string currencyCode,decimal fxRateToIrr,string gateway,string gatewayTransactionId,DateTime paidAtUtc)
    {
        if(amountIrr<=0 || fxRateToIrr<=0 || string.IsNullOrWhiteSpace(gatewayTransactionId)) throw new DomainException("Invalid payment.");
        return new Payment { Id=id, OrderId=orderId, PaymentAttemptId=paymentAttemptId, AmountIRR=amountIrr, CurrencyCode=currencyCode.Trim().ToUpperInvariant(), FxRateToIRR=fxRateToIrr, Gateway=gateway.Trim(), GatewayTransactionId=gatewayTransactionId.Trim(), PaidAtUtc=paidAtUtc };
    }
}
