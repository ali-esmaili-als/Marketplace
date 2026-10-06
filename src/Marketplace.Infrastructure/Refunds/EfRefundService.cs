using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Refunds.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Refunds;

public sealed class EfRefundService(
    MarketplaceDbContext db,
    IIdGenerator ids, ICurrentUser currentUser) : IRefundService
{
    public async Task<long> CreateAsync(
        long orderId, long paymentId, long amountIRR, string? reason,
        IReadOnlyList<RefundLineRequest> items,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required.");
        if (items.Count == 0)
            throw new InvalidOperationException("At least one refund item is required.");

        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Order not found.");
            if (order.CustomerId != currentUser.UserId)
                throw new UnauthorizedAccessException("You cannot refund this order.");

            var payment = await db.Payments.SingleOrDefaultAsync(
                x => x.Id == paymentId && x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Payment not found.");

            var orderItems = await db.OrderItems
                .Where(x => x.OrderId == orderId)
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            var completedRefund = await db.Refunds
                .Where(x => x.PaymentId == paymentId && x.Status == RefundStatus.Completed)
                .SumAsync(x => (long?)x.AmountIRR, cancellationToken) ?? 0;

            if (amountIRR <= 0 || completedRefund + amountIRR > payment.AmountIRR)
                throw new InvalidOperationException("Refund exceeds captured payment.");

            var refund = Refund.Create(ids.NewId(), orderId, paymentId, amountIRR, reason);

            long calculatedItemTotal = 0;
            foreach (var line in items)
            {
                if (!orderItems.TryGetValue(line.OrderItemId, out var orderItem))
                    throw new InvalidOperationException("Refund item does not belong to the order.");

                if (line.Quantity <= 0 || line.Quantity > orderItem.Quantity)
                    throw new InvalidOperationException("Invalid refund quantity.");

                var amount = checked((long)Math.Round(
                    (decimal)orderItem.FinalLineTotalIRR * line.Quantity / orderItem.Quantity,
                    MidpointRounding.AwayFromZero));

                calculatedItemTotal += amount;

                refund.AddItem(RefundItem.Create(
                    ids.NewId(), refund.Id, orderItem.Id, line.Quantity, amount,
                    line.InventoryDisposition));
            }

            if (calculatedItemTotal != amountIRR)
                throw new InvalidOperationException("Refund amount does not match refund items.");

            refund.StartProcessing();

            var commission = await db.Commissions.SingleOrDefaultAsync(
                x => x.OrderId == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Commission not found.");

            var previousReversal = await db.CommissionReversals
                .Where(x => x.CommissionId == commission.Id)
                .SumAsync(x => (long?)x.ReversedCommissionIRR, cancellationToken) ?? 0;

            var maximumReversible = Math.Max(0, commission.CommissionAmountIRR - previousReversal);
            var reversed = Math.Min(
                maximumReversible,
                (long)Math.Floor(
                    (decimal)commission.CommissionAmountIRR * amountIRR / order.TotalAmountIRR));

            db.CommissionReversals.Add(CommissionReversal.Create(
                ids.NewId(), commission.Id, orderId, refund.Id, amountIRR, reversed));

            var balance = await db.SellerBalances.SingleOrDefaultAsync(
                x => x.SellerId == commission.SellerId, cancellationToken)
                ?? throw new InvalidOperationException("Seller balance not found.");

            var sellerDebit = amountIRR - reversed;
            if (sellerDebit > 0)
            {
                var withdrawable = balance.WithdrawableIRR;
                var fromAvailable = Math.Min(sellerDebit, withdrawable);

                if (fromAvailable > 0)
                    balance.RemoveAvailable(fromAvailable);

                var liability = sellerDebit - fromAvailable;
                if (liability > 0)
                    balance.AddLiability(liability);
            }

            if (completedRefund + amountIRR == payment.AmountIRR)
                order.MarkRefunded();
            else
                order.MarkPartiallyRefunded();

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