namespace Marketplace.Application.Checkout.Ports; public interface ICouponReservationService{Task<CouponReservationResult?> ReserveAsync(long customerId,long cartId,long orderId,string couponCode,long eligibleAmountIRR,CancellationToken cancellationToken=default);
    Task MarkUsedAsync(long orderId, CancellationToken cancellationToken=default);
    Task ReleaseAsync(long orderId, CancellationToken cancellationToken=default);} public sealed record CouponReservationResult(long CouponId,string CouponCode,long DiscountIRR);
