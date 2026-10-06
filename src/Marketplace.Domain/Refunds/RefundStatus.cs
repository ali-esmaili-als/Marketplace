namespace Marketplace.Domain.Refunds;
public enum RefundStatus:byte { Requested=1,Approved=2,Processing=3,Completed=4,Failed=5,Rejected=6 }