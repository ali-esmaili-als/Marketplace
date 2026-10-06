using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class CommissionReversal : Entity<long>
{
    private CommissionReversal(){}
    public long CommissionId{get;private set;} public long OrderId{get;private set;} public long RefundId{get;private set;} public long RefundAmountIRR{get;private set;} public long ReversedCommissionIRR{get;private set;} public DateTime CreatedAtUtc{get;private set;}
    public static CommissionReversal Create(long id,long commissionId,long orderId,long refundId,long refundAmountIrr,long reversedCommissionIrr){if(id<=0||commissionId<=0||orderId<=0||refundId<=0||refundAmountIrr<=0||reversedCommissionIrr<0||reversedCommissionIrr>refundAmountIrr)throw new DomainException("Invalid commission reversal.");return new CommissionReversal{Id=id,CommissionId=commissionId,OrderId=orderId,RefundId=refundId,RefundAmountIRR=refundAmountIrr,ReversedCommissionIRR=reversedCommissionIrr,CreatedAtUtc=DateTime.UtcNow};}
}