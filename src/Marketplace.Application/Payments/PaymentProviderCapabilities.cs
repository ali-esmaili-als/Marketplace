using Marketplace.Domain.Payments;

namespace Marketplace.Application.Payments;

/// <summary>
/// Reports whether the gateway has a real protocol implementation in this build.
/// Configuration values alone do not make an adapter operational.
/// </summary>
public static class PaymentProviderCapabilities
{
    public static bool IsProtocolImplemented(PaymentProviderCode provider)
        => provider == PaymentProviderCode.TestBank;

    public static string ReadinessMessage(PaymentProviderCode provider)
        => provider == PaymentProviderCode.TestBank
            ? "Implemented test gateway. It is unavailable in Production."
            : "Official bank create/verify/refund protocol is not implemented; this provider must remain disabled and hidden.";
}