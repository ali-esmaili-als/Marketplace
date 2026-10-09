using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Marketplace.Application.Abstractions;

namespace Marketplace.Infrastructure.Persistence;

public sealed class SqlIdGenerator(MarketplaceDbContext db):IIdGenerator
{
    public async Task<long> NextAsync(CancellationToken ct=default)
    {
        // SQL Server forbids NEXT VALUE FOR inside the derived table that EF Core can
        // generate for SqlQueryRaw<T>(). Execute it as a scalar command instead.
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR dbo.MarketplaceSequence";
            if (db.Database.CurrentTransaction is { } transaction)
                command.Transaction = transaction.GetDbTransaction();

            var value = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt64(value);
        }
        finally
        {
            if (closeConnection)
                await db.Database.CloseConnectionAsync();
        }
    }
}
