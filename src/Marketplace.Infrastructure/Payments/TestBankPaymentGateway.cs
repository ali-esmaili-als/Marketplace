using System.Security.Cryptography;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Payments;

/// <summary>
/// A deterministic local test gateway. It never contacts a real bank.
/// The return URL points back to this API and carries a high-entropy authority.
/// </summary>
public sealed class TestBankPaymentGateway(string? returnBaseUrl) : IPaymentGateway
{
    public string ProviderName => "TEST_BANK";

    public Task<PaymentRedirect> CreatePaymentAsync(long paymentId, long orderId, long amountIRR, CancellationToken cancellationToken = default)
    {
        if (paymentId <= 0 || orderId <= 0 || amountIRR <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountIRR), "Payment details must be positive.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var authority = $"TEST-{paymentId}-{token}";
        var baseUrl = string.IsNullOrWhiteSpace(returnBaseUrl)
            ? "/api/payments/test-return"
            : $"{returnBaseUrl.TrimEnd('/')}/api/payments/test-return";
        var separator = baseUrl.Contains('?') ? "&" : "?";
        var url = $"{baseUrl}{separator}paymentId={paymentId}&authority={Uri.EscapeDataString(authority)}&result=success";
        return Task.FromResult(new PaymentRedirect(ProviderName, authority, url));
    }

    public Task<PaymentVerification> VerifyAsync(string authority, long amountIRR, CancellationToken cancellationToken = default)
    {
        var valid = amountIRR > 0 && !string.IsNullOrWhiteSpace(authority)
            && authority.StartsWith("TEST-", StringComparison.Ordinal);
        return Task.FromResult(valid
            ? new PaymentVerification(true, $"TEST-REF-{authority[^Math.Min(authority.Length, 12)..]}", null)
            : new PaymentVerification(false, null, "Invalid test-bank authority."));
    }

    public Task<bool> RefundAsync(string? paymentReference, long amountIRR, CancellationToken cancellationToken = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(paymentReference) && amountIRR > 0);
}
