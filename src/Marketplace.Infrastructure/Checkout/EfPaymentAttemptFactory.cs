using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Payments;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.Checkout;

public sealed class EfPaymentAttemptFactory(MarketplaceDbContext db, IIdGenerator ids) : IPaymentAttemptFactory
{
    public async Task<long> CreateAsync(PaymentAttemptRequest request, CancellationToken cancellationToken = default)
    {
        var id = ids.NewId();
        var attempt = PaymentAttempt.Create(id, request.OrderId, request.CustomerId, request.AmountIRR,
            request.CurrencyCode, request.FxRateToIRR, request.Gateway);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }
}