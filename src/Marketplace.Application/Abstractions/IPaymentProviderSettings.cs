namespace Marketplace.Application.Abstractions;

using Marketplace.Domain.Payments;

public sealed record PaymentProviderInfo(PaymentProviderCode Provider,string DisplayName,bool IsEnabled,bool IsVisible,int SortOrder);

public interface IPaymentProviderSettings
{
    Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct=default);
    Task<PaymentProviderInfo?> GetAsync(PaymentProviderCode provider,CancellationToken ct=default);
}