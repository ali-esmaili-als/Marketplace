using Marketplace.Application.Common.Abstractions;
using Marketplace.Application.Complaints.Ports;
using Marketplace.Domain.Complaints;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Complaints;

public sealed class EfComplaintService(MarketplaceDbContext db, IIdGenerator ids) : IComplaintService
{
    public async Task<long> OpenAsync(long orderId, long customerId, string subject, string description, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("Order does not belong to customer.");

        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == order.StoreId, cancellationToken)
            ?? throw new InvalidOperationException("Store not found.");

        var complaint = Complaint.Create(ids.NewId(), orderId, customerId, store.SellerId, subject, description);
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(cancellationToken);
        return complaint.Id;
    }
}