namespace Marketplace.Application.Abstractions;

public interface ISmsProvider
{
    string Name { get; }
    string DisplayName { get; }
    string CreateCode();
    Task SendOtpAsync(string mobile, string code, CancellationToken ct);
    Task SendMessageAsync(string mobile, string message, CancellationToken ct);
}