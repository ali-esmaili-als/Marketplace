using Marketplace.Domain.Common;
namespace Marketplace.Domain.Pricing;
public sealed class CouponProduct : Entity<long>
{
    private CouponProduct() { }
    public long CouponId { get; private set; }
    public long ProductId { get; private set; }
    public static CouponProduct Create(long id,long couponId,long productId)
    {
        if(id<=0||couponId<=0||productId<=0) throw new DomainException("Invalid coupon product scope.");
        return new CouponProduct { Id=id,CouponId=couponId,ProductId=productId };
    }
}