using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

public sealed class RefundLedgerIdentityMigrationTests
{
    [Fact]
    public async Task Refund_ledger_identity_migration_is_idempotent_and_restores_fk_and_filtered_unique_index()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");

        const string databaseName = "MarketplaceRefundLedgerMigrationTests";
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
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            var migrationPath = Path.Combine(AppContext.BaseDirectory, "database", "019_RefundLedgerIdentity.sql");
            Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script was not copied to test output: {bootstrapPath}");
            Assert.True(File.Exists(migrationPath), $"Refund ledger migration was not copied to test output: {migrationPath}");

            var bootstrap = await File.ReadAllTextAsync(bootstrapPath);
            var migration = await File.ReadAllTextAsync(migrationPath);

            await using var connection = new SqlConnection(targetBuilder.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, bootstrap);

            // Emulate an installation created before migration 019.
            await ExecuteAsync(connection, """
                DROP INDEX UX_BalanceTransactions_RefundId ON dbo.BalanceTransactions;
                ALTER TABLE dbo.BalanceTransactions DROP CONSTRAINT FK_BalanceTransactions_Refunds;
                ALTER TABLE dbo.BalanceTransactions DROP COLUMN RefundId;
                """);

            await ExecuteAsync(connection, migration);
            // Deployment retry must be safe.
            await ExecuteAsync(connection, migration);

            await using var verify = new SqlCommand("""
                SELECT
                    CASE WHEN COL_LENGTH(N'dbo.BalanceTransactions', N'RefundId') IS NOT NULL THEN 1 ELSE 0 END,
                    (SELECT COUNT(*) FROM sys.foreign_keys
                     WHERE parent_object_id = OBJECT_ID(N'dbo.BalanceTransactions')
                       AND name = N'FK_BalanceTransactions_Refunds'
                       AND referenced_object_id = OBJECT_ID(N'dbo.Refunds')
                       AND is_disabled = 0 AND is_not_trusted = 0),
                    (SELECT COUNT(*) FROM sys.indexes
                     WHERE object_id = OBJECT_ID(N'dbo.BalanceTransactions')
                       AND name = N'UX_BalanceTransactions_RefundId'
                       AND is_unique = 1 AND has_filter = 1
                       AND filter_definition LIKE N'%RefundId%IS NOT NULL%')
                """, connection);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
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

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
