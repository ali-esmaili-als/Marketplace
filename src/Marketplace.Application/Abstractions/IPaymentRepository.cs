using Marketplace.Domain.Payments;

namespace Marketplace.Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment?> GetAsync(long id,CancellationToken cancellationToken=default);
    Task<Payment?> GetByOrderAsync(long orderId,CancellationToken cancellationToken=default);
    Task<PaymentTransaction?> GetLatestTransactionAsync(long paymentId,CancellationToken cancellationToken=default);
    void AddTransaction(PaymentTransaction transaction);
    void Add(Payment payment);
}