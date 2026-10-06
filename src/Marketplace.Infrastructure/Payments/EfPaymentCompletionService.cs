using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Payments.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Payments;

public sealed class EfPaymentCompletionService(
    MarketplaceDbContext db,
    IIdGenerator ids,
    IClock clock,
    ISellerBalanceService sellerBalance,
    ICouponReservationService coupons) : IPaymentCompletionService
{
    public async Task CompleteAsync(
        long paymentAttemptId,
        string gatewayTransactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayTransactionId))
            throw new ArgumentException(
                "Gateway transaction id is required.",
                nameof(gatewayTransactionId));

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var attempt = await db.PaymentAttempts
                .SingleOrDefaultAsync(x => x.Id == paymentAttemptId, cancellationToken)
                ?? throw new InvalidOperationException("Payment attempt not found.");

            if (attempt.Status == PaymentAttemptStatus.Succeeded)
            {
                await tx.CommitAsync(cancellationToken);
                return;
            }

            attempt.Succeed(gatewayTransactionId);

            var order = await db.Orders
                .SingleAsync(x => x.Id == attempt.OrderId, cancellationToken);

            order.MarkPaid();

            var paymentExists = await db.Payments
                .AnyAsync(x => x.PaymentAttemptId == attempt.Id, cancellationToken);

            if (!paymentExists)
            {
                db.Payments.Add(
                    Payment.Create(
                        ids.NewId(),
                        order.Id,
                        attempt.Id,
                        attempt.AmountIRR,
                        attempt.CurrencyCode,
                        attempt.FxRateToIRR,
                        attempt.Gateway,
                        gatewayTransactionId,
                        clock.UtcNow));
            }

            var commission = await db.Commissions
                .SingleOrDefaultAsync(
                    x => x.OrderId == order.Id,
                    cancellationToken);

            if (commission is null)
            {
                var store = await db.Stores
                    .SingleAsync(x => x.Id == order.StoreId, cancellationToken);

                var seller = await db.Sellers
                    .SingleAsync(x => x.Id == store.SellerId, cancellationToken);

                commission = Commission.Create(
                    ids.NewId(),
                    order.Id,
                    store.Id,
                    seller.Id,
                    order.TotalAmountIRR,
                    store.CommissionRate,
                    store.MinCommissionIRR);

                db.Commissions.Add(commission);
            }

            await coupons.MarkUsedAsync(order.Id, cancellationToken);

            await sellerBalance.AddPendingAsync(
                commission.SellerId,
                order.Id,
                commission.SellerAmountIRR,
                $"PAYMENT:PENDING:{attempt.Id}",
                cancellationToken);

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
