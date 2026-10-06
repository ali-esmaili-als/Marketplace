namespace Marketplace.Infrastructure.Payments;

public sealed record BankGatewayConfiguration(
    string CreateUrl,
    string VerifyUrl,
    string RefundUrl,
    string CallbackUrl,
    string MerchantId,
    string TerminalId,
    string Username,
    string Password,
    string? AdditionalJson);