using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Coupons;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Checkout;

public sealed class EfCouponReservationService(MarketplaceDbContext db, IIdGenerator ids, IClock clock)
    : ICouponReservationService
{
    public async Task<CouponReservationResult?> ReserveAsync(
        long customerId, long cartId, long orderId, string couponCode, long eligibleAmountIRR,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(couponCode) || eligibleAmountIRR <= 0)
            return null;

        var cart = await db.Carts.AsNoTracking()
            .Where(x => x.Id == cartId && x.CustomerId == customerId)
            .Select(x => new { x.StoreId })
            .SingleOrDefaultAsync(cancellationToken);

        if (cart is null) return null;

        var now = clock.UtcNow;
        var code = couponCode.Trim().ToUpperInvariant();
        var coupon = await db.Coupons.SingleOrDefaultAsync(
            x => x.StoreId == cart.StoreId && x.Code == code, cancellationToken);

        if (coupon is null || !coupon.IsActive ||
            (coupon.StartsAtUtc.HasValue && coupon.StartsAtUtc.Value > now) ||
            (coupon.ExpiresAtUtc.HasValue && coupon.ExpiresAtUtc.Value <= now))
            return null;

        if (coupon.MinimumCartAmountIRR.HasValue &&
            eligibleAmountIRR < coupon.MinimumCartAmountIRR.Value)
            return null;

        if (coupon.NewCustomerOnly)
        {
            var hasPreviousOrder = await db.Orders.AnyAsync(
                x => x.CustomerId == customerId && x.Status != Marketplace.Domain.Orders.OrderStatus.Cancelled,
                cancellationToken);
            if (hasPreviousOrder) return null;
        }

        if (coupon.MaxUsagePerCustomer.HasValue)
        {
            var customerUses = await db.CouponUsages.CountAsync(
                x => x.CouponId == coupon.Id &&
                     x.CustomerId == customerId &&
                     (x.Status == CouponUsageStatus.Reserved ||
                      x.Status == CouponUsageStatus.Used),
                cancellationToken);

            if (customerUses >= coupon.MaxUsagePerCustomer.Value)
                return null;
        }

        var reserved = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.Coupons
            SET ReservedUsageCount = ReservedUsageCount + 1
            WHERE Id = {coupon.Id}
              AND IsActive = 1
              AND (MaxUsageCount IS NULL OR ReservedUsageCount + UsedUsageCount < MaxUsageCount)
            """, cancellationToken);

        if (reserved != 1)
            return null;

        var usage = CouponUsage.Create(ids.NewId(), coupon.Id, customerId, orderId);
        db.CouponUsages.Add(usage);
        await db.SaveChangesAsync(cancellationToken);

        return new CouponReservationResult(
            coupon.Id,
            coupon.Code,
            coupon.CalculateDiscount(eligibleAmountIRR));
    }

    public async Task MarkUsedAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var usage = await db.CouponUsages.SingleOrDefaultAsync(
            x => x.OrderId == orderId && x.Status == CouponUsageStatus.Reserved,
            cancellationToken) ?? throw new InvalidOperationException("Coupon reservation not found.");

        var coupon = await db.Coupons.SingleAsync(x => x.Id == usage.CouponId, cancellationToken);
        coupon.MarkUsageUsed();
        usage.MarkUsed();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var usage = await db.CouponUsages.SingleOrDefaultAsync(
            x => x.OrderId == orderId && x.Status == CouponUsageStatus.Reserved,
            cancellationToken);

        if (usage is null) return;

        var coupon = await db.Coupons.SingleAsync(x => x.Id == usage.CouponId, cancellationToken);
        coupon.ReleaseUsage();
        usage.Release();
        await db.SaveChangesAsync(cancellationToken);
    }
}
