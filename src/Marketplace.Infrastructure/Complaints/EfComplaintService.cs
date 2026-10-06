using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Complaints.Ports;
using Marketplace.Domain.Complaints;
using Marketplace.Application.Finance.Ports;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Complaints;

public sealed class EfComplaintService(
    MarketplaceDbContext db,
    IIdGenerator ids, ICurrentUser currentUser, ISellerBalanceService sellerBalance) : IComplaintService
{
    public async Task<long> OpenAsync(
        long orderId, long customerId, string subject, string description,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Authentication is required.");
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (customerId != currentUser.UserId || order.CustomerId != currentUser.UserId)
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

            if (!currentUser.IsAuthenticated || complaint.CustomerId != currentUser.UserId) throw new UnauthorizedAccessException("Only the complaint customer can resolve for customer.");
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
                    await sellerBalance.BlockAsync(complaint.SellerId, order.Id, amount, $"COMPLAINT:{complaint.Id}", cancellationToken);
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

            if (!currentUser.IsAuthenticated || complaint.SellerId != currentUser.UserId) throw new UnauthorizedAccessException("Only the complaint seller can resolve for seller.");
            complaint.ResolveForSeller();

            var hold = await db.SellerBalanceHolds.SingleOrDefaultAsync(
                x => x.OrderId == complaint.OrderId &&
                     x.SellerId == complaint.SellerId &&
                     x.Status == SellerBalanceHoldStatus.Active,
                cancellationToken);

            if (hold is not null)
            {
                await sellerBalance.ReleaseBlockAsync(
                    complaint.SellerId, complaint.OrderId, hold.AmountIRR,
                    $"COMPLAINT:{complaint.Id}:RELEASE", cancellationToken);
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

        if (!currentUser.IsAuthenticated || (complaint.CustomerId != currentUser.UserId && complaint.SellerId != currentUser.UserId)) throw new UnauthorizedAccessException("Only a complaint participant can close it.");
        complaint.Close();
        await db.SaveChangesAsync(cancellationToken);
    }
}