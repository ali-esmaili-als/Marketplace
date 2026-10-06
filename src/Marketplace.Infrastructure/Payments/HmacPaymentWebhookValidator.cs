using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Payments.Ports;
using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Payments;

public sealed class HmacPaymentWebhookValidator(IConfiguration configuration) : IPaymentWebhookValidator
{
    public bool IsValid(long paymentAttemptId, string gatewayTransactionId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(gatewayTransactionId))
            return false;

        var secret = configuration["PaymentGateway:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var payload = $"{paymentAttemptId}:{gatewayTransactionId}";
        var expected = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(payload)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToUpperInvariant()));
    }
}