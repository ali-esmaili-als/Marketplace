namespace Marketplace.Application.Abstractions;

public sealed record SmsProviderInfo(string Provider, string DisplayName, bool IsEnabled, bool IsVisible, int SortOrder);

public interface ISmsProviderSettings
{
    Task<IReadOnlyList<SmsProviderInfo>> GetAllAsync(CancellationToken ct = default);
    Task<SmsProviderInfo?> GetSelectedAsync(CancellationToken ct = default);
}