using System.Data.Common;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

public sealed class FinancialSchemaConstraintTests
{
    [Fact]
    public async Task Sql_server_enforces_financial_check_constraints_and_unique_seller_balance()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "MARKETPLACE_SQLSERVER must point to the SQL Server integration-test database.");

        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new MarketplaceDbContext(options);
        await db.Database.EnsureCreatedAsync();

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.SellerBalances
                (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
            VALUES (910001, 910001, 100000, 0, 0, 25000, 0, SYSUTCDATETIME());
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.SellerBalances
                (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
            VALUES (910002, 910002, -1, 0, 0, 0, 0, SYSUTCDATETIME());
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.SellerBalances
                (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
            VALUES (910003, 910001, 1000, 0, 0, 0, 0, SYSUTCDATETIME());
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.Settlements
                (Id, SellerId, AmountIRR, Status, BankAccountId, BankNameSnapshot, IbanSnapshot,
                 AccountHolderNameSnapshot, RequestedAtUtc)
            VALUES (910004, 910001, 0, 1, 910001, N'Test Bank', N'IR000000000000000000000000',
                    N'Integration Test', SYSUTCDATETIME());
            """);

        var count = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.SellerBalances WHERE SellerId = 910001")
            .SingleAsync();
        Assert.Equal(1, count);

        // Inventory invariants must be enforced by SQL Server, not only by domain methods.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.InventoryItems (Id, ProductVariantId, StockQuantity, ReservedQuantity, IsActive)
            VALUES (920001, 920001, 5, 0, 1);
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.InventoryItems (Id, ProductVariantId, StockQuantity, ReservedQuantity, IsActive)
            VALUES (920002, 920002, 5, 6, 1);
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.InventoryReservations
                (Id, ProductVariantId, OrderId, Quantity, Status, ExpiresAtUtc, CreatedAtUtc)
            VALUES (920003, 920001, 920003, 0, 1, DATEADD(hour, 1, SYSUTCDATETIME()), SYSUTCDATETIME());
            """);

        // Two independent sessions compete for four units from stock of five.
        // The conditional UPDATE is atomic: only one reservation may succeed.
        async Task<int> TryReserveAsync()
        {
            await using var contender = new MarketplaceDbContext(options);
            return await contender.Database.ExecuteSqlRawAsync("""
                UPDATE dbo.InventoryItems
                SET ReservedQuantity = ReservedQuantity + 4
                WHERE Id = 920001
                  AND IsActive = 1
                  AND StockQuantity - ReservedQuantity >= 4;
                """);
        }

        var reservationResults = await Task.WhenAll(TryReserveAsync(), TryReserveAsync());
        Assert.Equal(1, reservationResults.Sum());

        var reserved = await db.Database.SqlQueryRaw<long>(
            "SELECT ReservedQuantity AS [Value] FROM dbo.InventoryItems WHERE Id = 920001")
            .SingleAsync();
        Assert.Equal(4L, reserved);
    }

    private static async Task AssertDatabaseConstraintAsync(MarketplaceDbContext db, string sql)
    {
        var thrown = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (DbException)
        {
            thrown = true;
        }

        Assert.True(thrown, "Expected SQL Server to reject a row that violates a financial constraint.");
    }
}
