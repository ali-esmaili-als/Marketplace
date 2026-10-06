namespace Marketplace.Application.Checkout.Results; public sealed record CheckoutPayResult(long OrderId,long PaymentAttemptId,long AmountIRR,string CurrencyCode,decimal FxRateToIRR);
