namespace Marketplace.Domain.Complaints;

public enum ComplaintStatus : byte
{
    Open = 1,
    UnderReview = 2,
    ResolvedForCustomer = 3,
    ResolvedForSeller = 4,
    Closed = 5
}