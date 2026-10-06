namespace Marketplace.Domain.Coupons;

public enum CouponUsageStatus : byte
{
    Reserved = 1,
    Used = 2,
    Released = 3,
    Expired = 4
}
