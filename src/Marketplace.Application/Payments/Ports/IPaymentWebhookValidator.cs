namespace Marketplace.Application.Payments.Ports;

public interface IPaymentWebhookValidator
{
    bool IsValid(long paymentAttemptId, string gatewayTransactionId, string? signature);
}