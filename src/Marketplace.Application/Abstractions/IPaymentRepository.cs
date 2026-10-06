using Marketplace.Domain.Payments;

namespace Marketplace.Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment?> GetAsync(long id,CancellationToken cancellationToken=default);
    Task<Payment?> GetByOrderAsync(long orderId,CancellationToken cancellationToken=default);
    void Add(Payment payment);
}