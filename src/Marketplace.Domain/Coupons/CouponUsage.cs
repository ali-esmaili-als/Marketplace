using Marketplace.Domain.Common;

namespace Marketplace.Domain.Coupons;

public sealed class CouponUsage : Entity<long>
{
    private CouponUsage() { }
    public long CouponId { get; private set; }
    public long CustomerId { get; private set; }
    public long OrderId { get; private set; }
    public CouponUsageStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static CouponUsage Create(long id,long couponId,long customerId,long orderId)
        => new() { Id=id, CouponId=couponId, CustomerId=customerId, OrderId=orderId, Status=CouponUsageStatus.Reserved, CreatedAtUtc=DateTime.UtcNow };
    public void MarkUsed(){Require(CouponUsageStatus.Reserved);Status=CouponUsageStatus.Used;CompletedAtUtc=DateTime.UtcNow;}
    public void Release(){Require(CouponUsageStatus.Reserved);Status=CouponUsageStatus.Released;CompletedAtUtc=DateTime.UtcNow;}
    public void Expire(){Require(CouponUsageStatus.Reserved);Status=CouponUsageStatus.Expired;CompletedAtUtc=DateTime.UtcNow;}
    private void Require(CouponUsageStatus status){if(Status!=status)throw new DomainException($"Coupon usage must be in {status} status.");}
}
