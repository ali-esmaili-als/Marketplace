using Marketplace.Domain.Common;

namespace Marketplace.Domain.Pricing;

public sealed class CouponUsage : Entity<long>
{
    private CouponUsage() { }
    public long CouponId { get; private set; }
    public long CustomerId { get; private set; }
    public long OrderId { get; private set; }
    public long DiscountAmountIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static CouponUsage Create(long id,long couponId,long customerId,long orderId,long discountAmountIrr)
    {
        if(id<=0||couponId<=0||customerId<=0||orderId<=0||discountAmountIrr<0) throw new DomainException("Invalid coupon usage.");
        return new CouponUsage { Id=id,CouponId=couponId,CustomerId=customerId,OrderId=orderId,DiscountAmountIRR=discountAmountIrr,CreatedAtUtc=DateTime.UtcNow };
    }
}