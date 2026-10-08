using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Sms;

public sealed class TestSmsProvider : ISmsProvider
{
    public string Name => "Test";
    public string DisplayName => "سامانه پیامکی تست";
    public string CreateCode() => "1234";
    public Task SendAsync(string mobile, string message, CancellationToken ct) => Task.CompletedTask;
}