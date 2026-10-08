namespace Marketplace.Application.Abstractions;

public interface ISmsProvider
{
    string Name { get; }
    string DisplayName { get; }
    Task SendAsync(string mobile, string message, CancellationToken ct);
    string CreateCode();
}