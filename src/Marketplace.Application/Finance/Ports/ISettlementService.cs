namespace Marketplace.Application.Finance.Ports;

public interface ISettlementService
{
    Task<long> RequestAsync(
        long sellerId,
        long bankAccountId,
        long amountIRR,
        CancellationToken cancellationToken = default);

    Task MarkProcessingAsync(
        long settlementId,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        long settlementId,
        string gatewayReference,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        long settlementId,
        string reason,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        long settlementId,
        CancellationToken cancellationToken = default);
}
