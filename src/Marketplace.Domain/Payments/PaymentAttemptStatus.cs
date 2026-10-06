namespace Marketplace.Domain.Payments;

public enum PaymentAttemptStatus : byte
{
    Pending=1,
    Redirected=2,
    Succeeded=3,
    Failed=4,
    Cancelled=5,
    Expired=6
}
