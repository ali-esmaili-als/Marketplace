using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Complaints.Ports;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Complaints;

public sealed class EfComplaintService(
    MarketplaceDbContext db,
    IIdGenerator ids) : IComplaintService
{
    public async Task<long> OpenAsync(
        long orderId, long customerId, string subject, string description,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("Order does not belong to customer.");

        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == order.StoreId, cancellationToken)
            ?? throw new InvalidOperationException("Store not found.");

        var complaint = Complaint.Create(
            ids.NewId(), orderId, customerId, store.SellerId, subject, description);

        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(cancellationToken);
        return complaint.Id;
    }

    public async Task ResolveForCustomerAsync(long complaintId, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var complaint = await db.Complaints.SingleOrDefaultAsync(
                x => x.Id == complaintId, cancellationToken)
                ?? throw new InvalidOperationException("Complaint not found.");

            complaint.ResolveForCustomer();

            var order = await db.Orders.SingleAsync(x => x.Id == complaint.OrderId, cancellationToken);
            var commission = await db.Commissions.SingleAsync(x => x.OrderId == order.Id, cancellationToken);
            var balance = await db.SellerBalances.SingleAsync(
                x => x.SellerId == complaint.SellerId, cancellationToken);

            var activeHold = await db.SellerBalanceHolds.SingleOrDefaultAsync(
                x => x.OrderId == order.Id &&
                     x.SellerId == complaint.SellerId &&
                     x.Status == SellerBalanceHoldStatus.Active,
                cancellationToken);

            if (activeHold is null)
            {
                var amount = Math.Min(balance.WithdrawableIRR, commission.SellerAmountIRR);
                if (amount > 0)
                {
                    balance.Block(amount);
                    db.SellerBalanceHolds.Add(SellerBalanceHold.Create(
                        ids.NewId(), complaint.SellerId, order.Id, amount, $"COMPLAINT:{complaint.Id}"));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ResolveForSellerAsync(long complaintId, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var complaint = await db.Complaints.SingleOrDefaultAsync(
                x => x.Id == complaintId, cancellationToken)
                ?? throw new InvalidOperationException("Complaint not found.");

            complaint.ResolveForSeller();

            var hold = await db.SellerBalanceHolds.SingleOrDefaultAsync(
                x => x.OrderId == complaint.OrderId &&
                     x.SellerId == complaint.SellerId &&
                     x.Status == SellerBalanceHoldStatus.Active,
                cancellationToken);

            if (hold is not null)
            {
                var balance = await db.SellerBalances.SingleAsync(
                    x => x.SellerId == complaint.SellerId, cancellationToken);
                balance.ReleaseBlock(hold.AmountIRR);
                hold.Release();
            }

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CloseAsync(long complaintId, CancellationToken cancellationToken = default)
    {
        var complaint = await db.Complaints.SingleOrDefaultAsync(
            x => x.Id == complaintId, cancellationToken)
            ?? throw new InvalidOperationException("Complaint not found.");

        complaint.Close();
        await db.SaveChangesAsync(cancellationToken);
    }
}