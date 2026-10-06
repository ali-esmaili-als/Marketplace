using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Payments.Ports;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Payments;

public sealed class EfPaymentLifecycleService(
    MarketplaceDbContext db,
    IInventoryReservationService inventory,
    ICouponReservationService coupons) : IPaymentLifecycleService
{
    public Task FailAsync(long paymentAttemptId, CancellationToken cancellationToken = default)
        => TransitionAsync(paymentAttemptId, PaymentAttemptStatus.Failed, cancellationToken);

    public Task CancelAsync(long paymentAttemptId, CancellationToken cancellationToken = default)
        => TransitionAsync(paymentAttemptId, PaymentAttemptStatus.Cancelled, cancellationToken);

    public Task ExpireAsync(long paymentAttemptId, CancellationToken cancellationToken = default)
        => TransitionAsync(paymentAttemptId, PaymentAttemptStatus.Expired, cancellationToken);

    private async Task TransitionAsync(
        long paymentAttemptId,
        PaymentAttemptStatus target,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(
                x => x.Id == paymentAttemptId, cancellationToken)
                ?? throw new InvalidOperationException("Payment attempt not found.");

            if (attempt.Status == target)
            {
                await tx.CommitAsync(cancellationToken);
                return;
            }

            if (attempt.Status == PaymentAttemptStatus.Succeeded)
                throw new InvalidOperationException("A succeeded payment attempt cannot be changed.");

            var order = await db.Orders.SingleAsync(
                x => x.Id == attempt.OrderId, cancellationToken);

            switch (target)
            {
                case PaymentAttemptStatus.Failed:
                    attempt.Fail();
                    break;
                case PaymentAttemptStatus.Cancelled:
                    attempt.Cancel();
                    break;
                case PaymentAttemptStatus.Expired:
                    attempt.Expire();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }

            await coupons.ReleaseAsync(order.Id, cancellationToken);
            await inventory.ReleaseAsync(
                order.Id,
                target == PaymentAttemptStatus.Expired,
                cancellationToken);

            if (order.Status == OrderStatus.PendingPayment)
                order.Cancel();

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}