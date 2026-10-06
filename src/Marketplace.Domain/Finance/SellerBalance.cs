using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class SellerBalance : AggregateRoot<long>
{
    private SellerBalance() { }
    public long SellerId { get; private set; }
    public long AvailableIRR { get; private set; }
    public long PendingIRR { get; private set; }
    public long BlockedIRR { get; private set; }
    public long ReservedForSettlementIRR { get; private set; }
    public long LiabilityIRR { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long WithdrawableIRR => Math.Max(0, AvailableIRR - ReservedForSettlementIRR);
    public static SellerBalance Create(long id,long sellerId)=>new(){Id=id,SellerId=sellerId,UpdatedAtUtc=DateTime.UtcNow};
    public void AddPending(long amount){Positive(amount);PendingIRR=checked(PendingIRR+amount);Touch();}
    public void ReleasePending(long amount){Positive(amount);if(PendingIRR<amount)throw new DomainException("Insufficient pending balance.");PendingIRR-=amount;AvailableIRR=checked(AvailableIRR+amount);Touch();}
    public void RemovePending(long amount){Positive(amount);if(PendingIRR<amount)throw new DomainException("Insufficient pending balance.");PendingIRR-=amount;Touch();}
    public void Block(long amount){Positive(amount);if(AvailableIRR<amount)throw new DomainException("Insufficient available balance.");AvailableIRR-=amount;BlockedIRR=checked(BlockedIRR+amount);Touch();}
    public void ReleaseBlock(long amount){Positive(amount);if(BlockedIRR<amount)throw new DomainException("Insufficient blocked balance.");BlockedIRR-=amount;AvailableIRR=checked(AvailableIRR+amount);Touch();}
    public void ConsumeBlock(long amount){Positive(amount);if(BlockedIRR<amount)throw new DomainException("Insufficient blocked balance.");BlockedIRR-=amount;Touch();}
    public void ReserveForSettlement(long amount){Positive(amount);if(WithdrawableIRR<amount)throw new DomainException("Insufficient withdrawable balance.");ReservedForSettlementIRR=checked(ReservedForSettlementIRR+amount);Touch();}
    public void CompleteSettlement(long amount){Positive(amount);if(ReservedForSettlementIRR<amount)throw new DomainException("Insufficient reserved settlement balance.");ReservedForSettlementIRR-=amount;Touch();}
    public void FailSettlement(long amount){Positive(amount);if(ReservedForSettlementIRR<amount)throw new DomainException("Insufficient reserved settlement balance.");ReservedForSettlementIRR-=amount;AvailableIRR=checked(AvailableIRR+amount);Touch();}
    public void AddLiability(long amount){Positive(amount);LiabilityIRR=checked(LiabilityIRR+amount);Touch();}
    public void RepayLiability(long amount){Positive(amount);if(LiabilityIRR<amount)throw new DomainException("Liability cannot be negative.");LiabilityIRR-=amount;Touch();}
    public void AddAvailable(long amount){Positive(amount);AvailableIRR=checked(AvailableIRR+amount);Touch();}
    public void RemoveAvailable(long amount){Positive(amount);if(AvailableIRR<amount)throw new DomainException("Insufficient available balance.");AvailableIRR-=amount;Touch();}
    private static void Positive(long amount){if(amount<=0)throw new DomainException("Amount must be positive.");}
    private void Touch()=>UpdatedAtUtc=DateTime.UtcNow;
}