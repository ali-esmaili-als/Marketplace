namespace Marketplace.Domain.Finance;

public enum BalanceTransactionType : byte
{
    Sale = 1,
    Commission = 2,
    Refund = 3,
    Settlement = 4,
    Adjustment = 5,
    Reversal = 6,
    ReleasePending = 7,
    CommissionReversal = 8,
    PendingReleased = 9,
    ComplaintHold = 10,
    ComplaintHoldReleased = 11,
    ComplaintHoldConsumed = 12,
    PendingRemoved = 13
}
