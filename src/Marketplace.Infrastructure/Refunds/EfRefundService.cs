using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Refunds.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Refunds;

public sealed class EfRefundService(
    MarketplaceDbContext db,
    IIdGenerator ids) : IRefundService
{
    public async Task<long> CreateAsync(long orderId, long paymentId, long amountIRR, string? reason, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Order not found.");

            var payment = await db.Payments.SingleOrDefaultAsync(
                x => x.Id == paymentId && x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Payment not found.");

            var alreadyRefunded = await db.Refunds
                .Where(x => x.PaymentId == paymentId && x.Status == RefundStatus.Completed)
                .SumAsync(x => (long?)x.AmountIRR, cancellationToken) ?? 0;

            if (alreadyRefunded + amountIRR > payment.AmountIRR)
                throw new InvalidOperationException("Refund exceeds captured payment.");

            var refund = Refund.Create(ids.NewId(), orderId, paymentId, amountIRR, reason);
            refund.StartProcessing();

            var commission = await db.Commissions.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Commission not found.");

            var reversed = Math.Min(
                commission.CommissionAmountIRR,
                (long)Math.Floor((decimal)commission.CommissionAmountIRR * amountIRR / order.TotalAmountIRR));

            db.CommissionReversals.Add(CommissionReversal.Create(
                ids.NewId(), commission.Id, orderId, refund.Id, amountIRR, reversed));

            var balance = await db.SellerBalances.SingleOrDefaultAsync(
                x => x.SellerId == commission.SellerId, cancellationToken)
                ?? throw new InvalidOperationException("Seller balance not found.");

            var sellerDebit = amountIRR - reversed;
            if (sellerDebit > 0)
            {
                var withdrawable = balance.AvailableIRR - balance.ReservedForSettlementIRR;
                if (sellerDebit <= withdrawable)
                    balance.RemoveAvailable(sellerDebit);
                else
                    balance.AddLiability(sellerDebit - Math.Max(0, withdrawable));
            }

            refund.Complete();
            db.Refunds.Add(refund);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return refund.Id;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}