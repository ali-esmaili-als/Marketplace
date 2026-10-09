using Marketplace.Domain.Common;

namespace Marketplace.Domain.Finance;

public sealed class Commission : AggregateRoot<long>
{
    private Commission() { }
    public long OrderId { get; private set; }
    public long StoreId { get; private set; }
    public long SellerId { get; private set; }
    public long OrderAmountIRR { get; private set; }
    public decimal CommissionRate { get; private set; }
    public long MinimumCommissionIRR { get; private set; }
    public long CalculatedCommissionIRR { get; private set; }
    public long CommissionAmountIRR { get; private set; }
    public long SellerAmountIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Commission Create(long id,long orderId,long storeId,long sellerId,long orderAmountIrr,decimal rate,long minimumCommissionIrr,long shippingFeeIrr=0)
    {
        if(orderAmountIrr<0 || shippingFeeIrr<0 || shippingFeeIrr>orderAmountIrr || rate<0 || rate>100 || minimumCommissionIrr<0) throw new DomainException("Invalid commission values.");
        var commissionableAmount=orderAmountIrr-shippingFeeIrr;
        var calculated=checked((long)Math.Floor(commissionableAmount*rate/100m));
        var final=Math.Max(calculated,minimumCommissionIrr);
        if(final>commissionableAmount) final=commissionableAmount;
        return new Commission { Id=id, OrderId=orderId, StoreId=storeId, SellerId=sellerId, OrderAmountIRR=orderAmountIrr, CommissionRate=rate, MinimumCommissionIRR=minimumCommissionIrr, CalculatedCommissionIRR=calculated, CommissionAmountIRR=final, SellerAmountIRR=orderAmountIrr-final, CreatedAtUtc=DateTime.UtcNow };
    }
}
