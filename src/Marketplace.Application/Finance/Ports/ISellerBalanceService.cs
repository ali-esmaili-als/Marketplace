using Marketplace.Domain.Finance;

namespace Marketplace.Application.Finance.Ports;

public interface ISellerBalanceService
{
    Task AddPendingAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default);

    Task ReleasePendingAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default);

    Task BlockAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default);

    Task ReleaseBlockAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default);

    Task DebitAvailableAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        BalanceTransactionType transactionType,
        string reference,
        CancellationToken cancellationToken = default);

    Task ReserveForSettlementAsync(long sellerId, long settlementId, long amountIRR, string reference, CancellationToken cancellationToken = default);

    Task AddLiabilityAsync(
        long sellerId,
        long orderId,
        long amountIRR,
        string reference,
        CancellationToken cancellationToken = default);
}
