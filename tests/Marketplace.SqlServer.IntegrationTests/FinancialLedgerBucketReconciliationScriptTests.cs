using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Executes the read-only ledger/balance diagnostics against a real SQL Server schema.
/// This catches SQL syntax and schema drift in the operational reconciliation script.
/// </summary>
public sealed class FinancialLedgerBucketReconciliationScriptTests
{
    [Fact]
    public async Task Diagnostics_execute_against_bootstrapped_schema_without_changing_financial_rows()
    {
        var configured = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(configured), "MARKETPLACE_SQLSERVER is required.");

        var databaseName = "MarketplaceLedgerDiagnostics_" + Guid.NewGuid().ToString("N");
        var masterConnectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        var targetConnectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName }.ConnectionString;

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}];", master);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var connection = new SqlConnection(targetConnectionString))
            {
                await connection.OpenAsync();
                var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
                var diagnosticsPath = Path.Combine(AppContext.BaseDirectory, "database", "FinancialLedgerBucketReconciliation.sql");
                Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script not found: {bootstrapPath}");
                Assert.True(File.Exists(diagnosticsPath), $"Ledger diagnostics script not found: {diagnosticsPath}");

                await using (var bootstrap = new SqlCommand(await File.ReadAllTextAsync(bootstrapPath), connection)
                {
                    CommandTimeout = 120
                })
                {
                    await bootstrap.ExecuteNonQueryAsync();
                }

                var before = await ReadFinancialRowCountsAsync(connection);
                await using (var diagnostics = new SqlCommand(await File.ReadAllTextAsync(diagnosticsPath), connection)
                {
                    CommandTimeout = 120
                })
                {
                    await diagnostics.ExecuteNonQueryAsync();
                }
                var after = await ReadFinancialRowCountsAsync(connection);

                Assert.Equal(before, after);
            }
        }
        finally
        {
            await using var master = new SqlConnection(masterConnectionString);
            await master.OpenAsync();
            await using var drop = new SqlCommand($"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """, master);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long[]> ReadFinancialRowCountsAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.SellerBalances),
                (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions),
                (SELECT COUNT_BIG(*) FROM dbo.Settlements),
                (SELECT COUNT_BIG(*) FROM dbo.SettlementReconciliationAudits),
                (SELECT COUNT_BIG(*) FROM dbo.OutboxMessages);
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, 5).Select(reader.GetInt64).ToArray();
    }
}
