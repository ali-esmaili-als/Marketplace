namespace Marketplace.Domain.Finance;

public enum SettlementStatus : byte { Requested=1, Processing=2, Completed=3, Failed=4, Cancelled=5, OnHold=6 }
