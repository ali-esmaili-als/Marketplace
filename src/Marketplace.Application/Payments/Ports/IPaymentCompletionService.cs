namespace Marketplace.Application.Payments.Ports;

public interface IPaymentCompletionService
{
    Task CompleteAsync(long paymentAttemptId, string gatewayTransactionId, CancellationToken cancellationToken = default);
}