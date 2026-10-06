using Marketplace.Domain.Finance;

namespace Marketplace.Application.Abstractions;

public interface ISellerPayoutGateway
{
    Task<(bool Success,string? Reference,string? Error)> TransferAsync(
        string bankName,string iban,string accountHolderName,long amountIRR,CancellationToken ct=default);
}

public sealed class NotConfiguredSellerPayoutGateway : ISellerPayoutGateway
{
    public Task<(bool Success,string? Reference,string? Error)> TransferAsync(string bankName,string iban,string accountHolderName,long amountIRR,CancellationToken ct=default)
        => throw new InvalidOperationException("Seller payout gateway is not configured.");
}