namespace Marketplace.Domain.Payments;

public enum PaymentStatus : byte { Pending=1, Redirected=2, Succeeded=3, Failed=4, Cancelled=5, Refunded=6, PartiallyRefunded=7 }