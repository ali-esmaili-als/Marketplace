using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Finance.Ports;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Refunds.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Complaints;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Refunds;

public sealed class EfRefundService(
    MarketplaceDbContext db,
    IIdGenerator ids, ICurrentUser currentUser, ISellerBalanceService sellerBalance, IInventoryReservationService inventory) : IRefundService
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

                var previouslyRefundedQuantity = await db.RefundItems
                    .Where(x => x.OrderItemId == orderItem.Id &&
                                db.Refunds.Any(r => r.Id == x.RefundId && r.Status == RefundStatus.Completed))
                    .SumAsync(x => (int?)x.Quantity, cancellationToken) ?? 0;

                if (previouslyRefundedQuantity + line.Quantity > orderItem.Quantity)
                    throw new InvalidOperationException("Refund quantity exceeds the remaining refundable quantity.");

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

            var sellerDebit = amountIRR - reversed;
            if (sellerDebit > 0)
            {
                var balance = await db.SellerBalances.SingleOrDefaultAsync(
                    x => x.SellerId == commission.SellerId, cancellationToken)
                    ?? throw new InvalidOperationException("Seller balance not found.");

                var fromAvailable = Math.Min(sellerDebit, balance.WithdrawableIRR);
                var liability = sellerDebit - fromAvailable;

                if (fromAvailable > 0)
                    await sellerBalance.DebitAvailableAsync(
                        commission.SellerId, orderId, fromAvailable,
                        BalanceTransactionType.Refund,
                        $"REFUND:{refund.Id}",
                        cancellationToken);

                if (liability > 0)
                    await sellerBalance.AddLiabilityAsync(
                        commission.SellerId, orderId, liability,
                        $"REFUND:{refund.Id}:LIABILITY",
                        cancellationToken);
            }

            await inventory.ApplyRefundAsync(
                orderId,
                refund.Items.Select(x => new InventoryRefundRequest(
                    orderItems[x.OrderItemId].ProductVariantId,
                    x.Quantity,
                    x.InventoryDisposition)).ToArray(),
                cancellationToken);

            if (completedRefund + amountIRR == payment.AmountIRR)
                order.MarkRefunded();
            else
                order.MarkPartiallyRefunded();

            refund.Complete($"INTERNAL:{refund.Id}");
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

    public async Task<long> CreateComplaintCompensationAsync(long complaintId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required.");

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
                ?? throw new InvalidOperationException("Complaint not found.");

            if (complaint.Status != ComplaintStatus.ResolvedForCustomer)
                throw new InvalidOperationException("Complaint is not resolved for customer.");
            if (complaint.CustomerId != currentUser.UserId)
                throw new UnauthorizedAccessException("Only the complaint customer can request compensation.");

            var payment = await db.Payments.Where(x => x.OrderId == complaint.OrderId)
                .OrderByDescending(x => x.PaidAtUtc).FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Successful payment not found.");

            var hold = await db.SellerBalanceHolds.SingleOrDefaultAsync(
                x => x.OrderId == complaint.OrderId && x.SellerId == complaint.SellerId &&
                     x.Status == SellerBalanceHoldStatus.Active, cancellationToken)
                ?? throw new InvalidOperationException("Active seller compensation hold not found.");

            var reason = $"COMPLAINT:{complaintId}";
            var existing = await db.Refunds.SingleOrDefaultAsync(
                x => x.OrderId == complaint.OrderId && x.PaymentId == payment.Id && x.Reason == reason,
                cancellationToken);

            if (existing is not null)
            {
                await tx.CommitAsync(cancellationToken);
                return existing.Id;
            }

            var refund = Refund.Create(ids.NewId(), complaint.OrderId, payment.Id, hold.AmountIRR, reason);
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

    public async Task CompleteAsync(long refundId, string gatewayRefundReference, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required.");
        if (string.IsNullOrWhiteSpace(gatewayRefundReference))
            throw new ArgumentException("Gateway refund reference is required.", nameof(gatewayRefundReference));

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var refund = await db.Refunds.SingleOrDefaultAsync(x => x.Id == refundId, cancellationToken)
                ?? throw new InvalidOperationException("Refund not found.");

            if (refund.Status == RefundStatus.Completed)
            {
                await tx.CommitAsync(cancellationToken);
                return;
            }

            refund.StartProcessing();

            var reason = refund.Reason;
            if (reason?.StartsWith("COMPLAINT:", StringComparison.Ordinal) == true)
            {
                if (!long.TryParse(reason["COMPLAINT:".Length..], out var complaintId))
                    throw new InvalidOperationException("Invalid complaint refund reference.");

                var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
                    ?? throw new InvalidOperationException("Complaint not found.");

                var hold = await db.SellerBalanceHolds.SingleOrDefaultAsync(
                    x => x.OrderId == complaint.OrderId && x.SellerId == complaint.SellerId &&
                         x.Status == SellerBalanceHoldStatus.Active, cancellationToken)
                    ?? throw new InvalidOperationException("Active seller compensation hold not found.");

                if (hold.AmountIRR != refund.AmountIRR)
                    throw new InvalidOperationException("Refund amount does not match complaint hold.");

                var commission = await db.Commissions.SingleAsync(x => x.OrderId == refund.OrderId, cancellationToken);
                var previousReversal = await db.CommissionReversals.Where(x => x.CommissionId == commission.Id)
                    .SumAsync(x => (long?)x.ReversedCommissionIRR, cancellationToken) ?? 0;

                var reversed = Math.Min(
                    Math.Max(0, commission.CommissionAmountIRR - previousReversal),
                    (long)Math.Floor((decimal)commission.CommissionAmountIRR * refund.AmountIRR /
                                     Math.Max(1, commission.OrderAmountIRR)));

                db.CommissionReversals.Add(CommissionReversal.Create(
                    ids.NewId(), commission.Id, refund.OrderId, refund.Id, refund.AmountIRR, reversed));

                await sellerBalance.ConsumeBlockAsync(
                    complaint.SellerId, complaint.OrderId, hold.AmountIRR,
                    $"REFUND:{refund.Id}:COMPLAINT", cancellationToken);
                hold.Consume();
            }

            refund.Complete(gatewayRefundReference);

            var payment = await db.Payments.SingleAsync(x => x.Id == refund.PaymentId, cancellationToken);
            var completedRefund = await db.Refunds
                .Where(x => x.PaymentId == refund.PaymentId && x.Status == RefundStatus.Completed && x.Id != refund.Id)
                .SumAsync(x => (long?)x.AmountIRR, cancellationToken) ?? 0;
            var order = await db.Orders.SingleAsync(x => x.Id == refund.OrderId, cancellationToken);

            if (completedRefund + refund.AmountIRR >= payment.AmountIRR)
                order.MarkRefunded();
            else
                order.MarkPartiallyRefunded();

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