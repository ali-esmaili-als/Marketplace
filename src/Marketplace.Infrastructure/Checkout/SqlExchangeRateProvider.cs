using Marketplace.Application.Checkout.Ports;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Checkout;

public sealed class SqlExchangeRateProvider(MarketplaceDbContext db) : IExchangeRateProvider
{
    public async Task<ExchangeRateSnapshot> GetLatestAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        var code = currencyCode.Trim().ToUpperInvariant();
        if (code == "IRR") return new ExchangeRateSnapshot("IRR", 1m);

        var row = await db.Database.SqlQueryRaw<ExchangeRateRow>(
            """
            SELECT TOP (1)
                CurrencyCode,
                RateToIRR
            FROM dbo.ExchangeRates
            WHERE CurrencyCode = {0}
              AND IsActive = 1
              AND EffectiveFromUtc <= SYSUTCDATETIME()
              AND (EffectiveToUtc IS NULL OR SYSUTCDATETIME() < EffectiveToUtc)
            ORDER BY EffectiveFromUtc DESC
            """, code).SingleOrDefaultAsync(cancellationToken);

        if (row is null) throw new InvalidOperationException($"No active exchange rate found for {code}.");
        return new ExchangeRateSnapshot(row.CurrencyCode, row.RateToIRR);
    }

    private sealed class ExchangeRateRow
    {
        public string CurrencyCode { get; init; } = null!;
        public decimal RateToIRR { get; init; }
    }
}