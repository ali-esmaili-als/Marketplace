namespace Marketplace.Application.Finance.Ports;

public interface IFinanceService
{
    Task ReleaseSellerFundsAsync(long orderId, CancellationToken cancellationToken = default);
}