namespace Marketplace.Domain.Finance;

public enum BalanceBucket : byte
{
    Available = 1,
    Pending = 2,
    Blocked = 3,
    ReservedForSettlement = 4,
    Liability = 5
}
