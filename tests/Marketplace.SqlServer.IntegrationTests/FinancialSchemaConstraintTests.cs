using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.IO;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

public sealed class FinancialSchemaConstraintTests
{
    [Fact]
    public async Task Complete_bootstrap_script_creates_real_schema_and_enforces_inventory_constraints()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");

        const string databaseName = "MarketplaceBootstrapIntegrationTests";
        var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await using var reset = new SqlCommand($"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                CREATE DATABASE [{databaseName}];
                """, master);
            await reset.ExecuteNonQueryAsync();
        }

        var targetBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };
        try
        {
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            Assert.True(File.Exists(scriptPath), $"Bootstrap SQL script was not copied to test output: {scriptPath}");
            var script = await File.ReadAllTextAsync(scriptPath);

            await using var connection = new SqlConnection(targetBuilder.ConnectionString);
            await connection.OpenAsync();
            await using (var applySchema = new SqlCommand(script, connection) { CommandTimeout = 120 })
            {
                await applySchema.ExecuteNonQueryAsync();
            }

            await using (var constraintCheck = new SqlCommand("""
                SELECT COUNT(*) FROM sys.check_constraints
                WHERE name = N'CK_InventoryItems_Qty' AND parent_object_id = OBJECT_ID(N'dbo.InventoryItems');
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await constraintCheck.ExecuteScalarAsync()));
            }

            await using var invalidInsert = new SqlCommand("""
                INSERT INTO dbo.InventoryItems (Id, ProductVariantId, StockQuantity, ReservedQuantity, IsActive)
                VALUES (940001, 940001, 5, 6, 1);
                """, connection);
            await Assert.ThrowsAsync<SqlException>(() => invalidInsert.ExecuteNonQueryAsync());

            // Contract checks catch drift between the documented bootstrap schema and
            // the invariants relied on by checkout, refunds, payments, and seller finance.
            await using (var contractCheck = new SqlCommand("""
                WITH RequiredObjects AS
                (
                    SELECT N'CK_Products_Price' AS ObjectName, N'CHECK' AS ObjectType UNION ALL
                    SELECT N'CK_Orders_Amounts', N'CHECK' UNION ALL
                    SELECT N'CK_Payments_Amount', N'CHECK' UNION ALL
                    SELECT N'CK_Refunds_Amount', N'CHECK' UNION ALL
                    SELECT N'CK_InventoryItems_Qty', N'CHECK' UNION ALL
                    SELECT N'CK_SellerBalances_NonNegative', N'CHECK' UNION ALL
                    SELECT N'CK_Settlements_Amount', N'CHECK' UNION ALL
                    SELECT N'UX_PaymentTransactions_Provider_Authority', N'INDEX' UNION ALL
                    SELECT N'UX_Refunds_OneActivePerOrder', N'INDEX' UNION ALL
                    SELECT N'UX_SellerBalanceHolds_OrderId', N'INDEX' UNION ALL
                    SELECT N'UX_BalanceTransactions_Order_Sale', N'INDEX'
                )
                SELECT COUNT(*)
                FROM RequiredObjects r
                WHERE
                    (r.ObjectType = N'CHECK' AND EXISTS
                        (SELECT 1 FROM sys.check_constraints c WHERE c.name = r.ObjectName))
                    OR
                    (r.ObjectType = N'INDEX' AND EXISTS
                        (SELECT 1 FROM sys.indexes i WHERE i.name = r.ObjectName AND i.is_unique = 1));
                """, connection))
            {
                Assert.Equal(11, Convert.ToInt32(await contractCheck.ExecuteScalarAsync()));
            }

            await using (var fkCheck = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.foreign_keys
                WHERE name IN
                (
                    N'FK_Orders_Users',
                    N'FK_OrderItems_Orders',
                    N'FK_Payments_Orders',
                    N'FK_Refunds_Orders',
                    N'FK_Settlements_Sellers'
                );
                """, connection))
            {
                Assert.Equal(5, Convert.ToInt32(await fkCheck.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await using var master = new SqlConnection(masterBuilder.ConnectionString);
            await master.OpenAsync();
            await using var cleanup = new SqlCommand($"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """, master);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

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

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.Payments (Id, OrderId, CustomerId, AmountIRR, Status, CreatedAtUtc)
            VALUES (930001, 930001, 930001, 0, 1, SYSUTCDATETIME());
            """);

        await AssertDatabaseConstraintAsync(db, """
            INSERT INTO dbo.Refunds
                (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
            VALUES (930002, 930002, 930002, 930002, 0, 1, 1, SYSUTCDATETIME());
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
