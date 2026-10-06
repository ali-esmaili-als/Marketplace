namespace Marketplace.Application.Payments.Ports;

public interface IPaymentLifecycleService
{
    Task FailAsync(long paymentAttemptId, CancellationToken cancellationToken = default);
    Task CancelAsync(long paymentAttemptId, CancellationToken cancellationToken = default);
    Task ExpireAsync(long paymentAttemptId, CancellationToken cancellationToken = default);
}