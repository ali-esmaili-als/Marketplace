using Marketplace.Domain.Orders;

namespace Marketplace.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetAsync(long id,CancellationToken cancellationToken=default);
    void Add(Order order);
}