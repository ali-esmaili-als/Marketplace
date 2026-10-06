using Marketplace.Domain.Common;
namespace Marketplace.Domain.Pricing;
public sealed class CouponCategory : Entity<long>
{
    private CouponCategory() { }
    public long CouponId { get; private set; }
    public long CategoryId { get; private set; }
    public static CouponCategory Create(long id,long couponId,long categoryId)
    {
        if(id<=0||couponId<=0||categoryId<=0) throw new DomainException("Invalid coupon category scope.");
        return new CouponCategory { Id=id,CouponId=couponId,CategoryId=categoryId };
    }
}