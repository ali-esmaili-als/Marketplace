using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Persistence;

public sealed class SqlIdGenerator(MarketplaceDbContext db):IIdGenerator
{
    public async Task<long> NextAsync(CancellationToken ct=default)
        => await db.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo.MarketplaceSequence").SingleAsync(ct);
}