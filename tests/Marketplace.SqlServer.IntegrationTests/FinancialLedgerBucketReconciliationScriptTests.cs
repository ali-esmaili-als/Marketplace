using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Executes the read-only ledger/balance diagnostics against a real SQL Server schema.
/// Seeded, intentionally inconsistent financial rows prove the checks detect the expected
/// findings while preserving all financial records and values.
/// </summary>
public sealed class FinancialLedgerBucketReconciliationScriptTests
{
    [Fact]
    public async Task Diagnostics_detect_seeded_ledger_and_settlement_findings_without_mutating_financial_rows()
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

                await SeedKnownFindingsAsync(connection);
                var before = await ReadFinancialSnapshotAsync(connection);
                List<List<string>> findings;
                await using (var diagnostics = new SqlCommand(await File.ReadAllTextAsync(diagnosticsPath), connection)
                {
                    CommandTimeout = 120
                })
                await using (var reader = await diagnostics.ExecuteReaderAsync())
                {
                    findings = await ReadFindingsAsync(reader);
                }
                var after = await ReadFinancialSnapshotAsync(connection);

                Assert.Equal(before.RowCounts, after.RowCounts);
                Assert.Equal(before.FinancialRows, after.FinancialRows);
                Assert.Equal(6, findings.Count);

                // FLR-01: second Available entry starts from 80, but the preceding entry ends at 100.
                Assert.Contains("910002", findings[0]);
                // FLR-02: current Available (70) differs from the latest Available ledger snapshot (50).
                Assert.Contains("910001", findings[1]);
                // FLR-03: non-zero Pending and ReservedForSettlement buckets have no bucket ledger history.
                Assert.Contains("910001", findings[2]);
                // FLR-04: reserved bucket is 40 while the active settlement total is 50.
                Assert.Contains("910001", findings[3]);
                // FLR-05: Requested settlement has no reservation posting.
                Assert.Contains("920001", findings[4]);
                // FLR-06: schema constraints prevent the deliberately seeded invalid bucket/snapshot row.
                Assert.Empty(findings[5]);
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

    private static async Task SeedKnownFindingsAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.Users (Id, Mobile, Email, PasswordHash, DisplayName, IsActive, IsMobileVerified, CreatedAtUtc)
            VALUES (910001, N'+989100000001', NULL, N'test-hash', N'Ledger Diagnostics Seller', 1, 1, '2026-01-01T00:00:00');

            INSERT dbo.Sellers (Id, UserId, Status, CreatedAtUtc, ActivatedAtUtc)
            VALUES (910001, 910001, 2, '2026-01-01T00:00:00', '2026-01-01T00:00:00');

            INSERT dbo.SellerBankAccounts
                (Id, SellerId, BankName, Iban, AccountHolderName, IsDefault, IsVerified, CreatedAtUtc)
            VALUES (930001, 910001, N'Test Bank', N'IR000000000000000000000000', N'Test Seller', 1, 1, '2026-01-01T00:00:00');

            INSERT dbo.SellerBalances
                (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
            VALUES
                (910001, 910001, 70, 25, 0, 40, 0, '2026-01-03T00:00:00');

            INSERT dbo.BalanceTransactions
                (Id, SellerId, OrderId, SettlementId, RefundId, Type, Bucket, AmountIRR,
                 BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
            VALUES
                (910001, 910001, NULL, NULL, NULL, 1, 1, 100, 0, 100, N'SEED_AVAILABLE_1', '2026-01-01T00:00:00'),
                (910002, 910001, NULL, NULL, NULL, 2, 1, 30, 80, 50, N'SEED_AVAILABLE_2', '2026-01-02T00:00:00');

            INSERT dbo.Settlements
                (Id, SellerId, RequestKey, AmountIRR, Status, BankAccountId, BankNameSnapshot,
                 IbanSnapshot, AccountHolderNameSnapshot, Reference, FailureReason, RequestedAtUtc, CompletedAtUtc)
            VALUES
                (920001, 910001, N'seed-request-920001', 50, 1, 930001, N'Test Bank',
                 N'IR000000000000000000000000', N'Test Seller', NULL, NULL, '2026-01-03T00:00:00', NULL);
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<List<string>>> ReadFindingsAsync(SqlDataReader reader)
    {
        var resultSets = new List<List<string>>();
        do
        {
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            }
            if (reader.FieldCount > 0)
                resultSets.Add(rows);
        } while (await reader.NextResultAsync());

        return resultSets;
    }

    private static async Task<(long[] RowCounts, string[] FinancialRows)> ReadFinancialSnapshotAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.SellerBalances),
                (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions),
                (SELECT COUNT_BIG(*) FROM dbo.Settlements),
                (SELECT COUNT_BIG(*) FROM dbo.SettlementReconciliationAudits),
                (SELECT COUNT_BIG(*) FROM dbo.OutboxMessages);

            SELECT N'B:' + CONCAT(Id, N'|', SellerId, N'|', AvailableIRR, N'|', PendingIRR, N'|',
                                  BlockedIRR, N'|', ReservedForSettlementIRR, N'|', LiabilityIRR)
            FROM dbo.SellerBalances ORDER BY Id;

            SELECT N'L:' + CONCAT(Id, N'|', SellerId, N'|', ISNULL(OrderId, 0), N'|',
                                  ISNULL(SettlementId, 0), N'|', ISNULL(RefundId, 0), N'|', Type, N'|',
                                  Bucket, N'|', AmountIRR, N'|', BalanceBeforeIRR, N'|', BalanceAfterIRR,
                                  N'|', ISNULL(Reference, N''), N'|', CONVERT(nvarchar(33), CreatedAtUtc, 126))
            FROM dbo.BalanceTransactions ORDER BY Id;

            SELECT N'S:' + CONCAT(Id, N'|', SellerId, N'|', ISNULL(RequestKey, N''), N'|', AmountIRR, N'|',
                                  Status, N'|', BankAccountId, N'|', BankNameSnapshot, N'|', IbanSnapshot,
                                  N'|', AccountHolderNameSnapshot, N'|', ISNULL(Reference, N''), N'|',
                                  ISNULL(FailureReason, N''), N'|', CONVERT(nvarchar(33), RequestedAtUtc, 126),
                                  N'|', ISNULL(CONVERT(nvarchar(33), CompletedAtUtc, 126), N''))
            FROM dbo.Settlements ORDER BY Id;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var counts = Enumerable.Range(0, 5).Select(reader.GetInt64).ToArray();
        var rows = new List<string>();
        while (await reader.NextResultAsync())
        {
            while (await reader.ReadAsync())
                rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
        return (counts, rows.ToArray());
    }
}
