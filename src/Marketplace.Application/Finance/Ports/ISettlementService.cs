namespace Marketplace.Application.Finance.Ports;

public interface ISettlementService
{
    Task<long> RequestAsync(long sellerId, long bankAccountId, long amountIRR, CancellationToken cancellationToken = default);
}