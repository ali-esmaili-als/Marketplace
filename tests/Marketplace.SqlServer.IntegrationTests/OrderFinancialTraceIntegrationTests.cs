using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Exercises a realistic order/payment/refund/commission/ledger chain against SQL Server.
/// This is a database relationship test, not a test of a live bank provider or the HTTP endpoint.
/// </summary>
public sealed class OrderFinancialTraceIntegrationTests
{
    [Fact]
    public async Task Financial_trace_links_order_payment_refund_commission_reversal_and_pooled_settlement()
    {
        var connectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");

        const string databaseName = "MarketplaceOrderFinancialTraceIntegrationTests";
        var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                CREATE DATABASE [{databaseName}];
                """);
        }

        var targetBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };
        try
        {
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script was not copied to test output: {bootstrapPath}");
            var bootstrap = await File.ReadAllTextAsync(bootstrapPath);

            await using var connection = new SqlConnection(targetBuilder.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, bootstrap);
            await ExecuteAsync(connection, """
                DECLARE @now DATETIME2(7) = SYSUTCDATETIME();

                INSERT dbo.Users(Id, Mobile, PasswordHash, DisplayName, CreatedAtUtc)
                VALUES
                    (71001, N'09120007101', N'test-hash-customer', N'Trace Customer', @now),
                    (71002, N'09120007102', N'test-hash-seller', N'Trace Seller', @now);

                INSERT dbo.Sellers(Id, UserId, Status, CommissionRateBasisPoints, MinimumCommissionIRR, MaxStoreCount, CreatedAtUtc, ActivatedAtUtc)
                VALUES (72001, 71002, 2, 1000, 0, 2, @now, @now);

                INSERT dbo.Stores(Id, SellerId, Name, Slug, Status, CommissionRateBasisPoints, MinimumCommissionIRR, CreatedAtUtc)
                VALUES (73001, 72001, N'Trace Store', N'trace-store', 2, 1000, 0, @now);

                INSERT dbo.SellerBankAccounts(Id, SellerId, BankName, Iban, AccountHolderName, IsDefault, IsVerified, CreatedAtUtc)
                VALUES (74001, 72001, N'Test Bank', N'IR000000000000000000000000', N'Trace Seller', 1, 1, @now);

                INSERT dbo.Orders
                    (Id, CustomerId, SellerId, StoreId, SubtotalAmountIRR, CampaignDiscountIRR, CouponDiscountIRR,
                     TotalAmountIRR, SellerAmountIRR, Status, CreatedAtUtc, PaidAtUtc)
                VALUES (75001, 71001, 72001, 73001, 1000000, 0, 0, 1000000, 900000, 8, @now, @now);

                INSERT dbo.Payments
                    (Id, OrderId, CustomerId, AmountIRR, Status, Provider, Authority, ReferenceNumber, CreatedAtUtc, PaidAtUtc, RefundedAtUtc)
                VALUES (76001, 75001, 71001, 1000000, 6, N'TestBank', N'AUTH-TRACE-1', N'PAY-TRACE-1', @now, @now, @now);

                INSERT dbo.PaymentTransactions(Id, PaymentId, AmountIRR, Status, Provider, Authority, Reference, CreatedAtUtc)
                VALUES (76101, 76001, 1000000, 3, N'TestBank', N'AUTH-TRACE-1', N'PAY-TRACE-1', @now);

                INSERT dbo.Commissions
                    (Id, OrderId, StoreId, SellerId, OrderAmountIRR, CommissionRate, MinimumCommissionIRR,
                     CalculatedCommissionIRR, CommissionAmountIRR, SellerAmountIRR, CreatedAtUtc)
                VALUES (77001, 75001, 73001, 72001, 1000000, 10, 0, 100000, 100000, 900000, @now);

                INSERT dbo.Refunds
                    (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, ProviderReference, RequestedAtUtc, CompletedAtUtc)
                VALUES (78001, 75001, 76001, 71001, 1000000, 1, 4, N'REFUND-TRACE-1', @now, @now);

                INSERT dbo.CommissionReversals
                    (Id, CommissionId, OrderId, RefundId, RefundAmountIRR, ReversedCommissionIRR, CreatedAtUtc)
                VALUES (79001, 77001, 75001, 78001, 1000000, 100000, @now);

                INSERT dbo.SellerBalances
                    (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
                VALUES (80001, 72001, 400000, 0, 0, 0, 0, @now);

                INSERT dbo.Settlements
                    (Id, SellerId, AmountIRR, Status, BankAccountId, BankNameSnapshot, IbanSnapshot,
                     AccountHolderNameSnapshot, Reference, RequestedAtUtc, CompletedAtUtc)
                VALUES (81001, 72001, 500000, 3, 74001, N'Test Bank', N'IR000000000000000000000000',
                        N'Trace Seller', N'SETTLE-TRACE-1', @now, @now);

                INSERT dbo.BalanceTransactions
                    (Id, SellerId, OrderId, SettlementId, RefundId, Type, Bucket, AmountIRR,
                     BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES
                    (82001, 72001, 75001, NULL, NULL, 1, 2, 900000, 0, 900000, N'SALE-TRACE-1', @now),
                    (82002, 72001, 75001, NULL, 78001, 3, 2, 1000000, 900000, 0, N'REFUND-TRACE-1', @now),
                    (82003, 72001, NULL, 81001, NULL, 4, 1, 500000, 900000, 400000, N'SETTLE-TRACE-1', @now),
                    (82004, 72001, 75001, NULL, NULL, 8, 2, 100000, 900000, 800000, N'REVERSAL-TRACE-1', @now);
                """);

            // Mirror the main links the admin trace relies on and verify that pooled settlement
            // activity is included through an explicit settlement ledger link, not by guessing
            // that the seller's payout belongs to the order.
            await using (var trace = new SqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM dbo.Orders o WHERE o.Id = 75001) AS OrderCount,
                    (SELECT COUNT(*) FROM dbo.Payments p WHERE p.OrderId = 75001) AS PaymentCount,
                    (SELECT COUNT(*) FROM dbo.PaymentTransactions pt JOIN dbo.Payments p ON p.Id = pt.PaymentId WHERE p.OrderId = 75001) AS ProviderTransactionCount,
                    (SELECT COUNT(*) FROM dbo.Commissions c WHERE c.OrderId = 75001 AND c.SellerId = 72001 AND c.OrderAmountIRR = 1000000 AND c.SellerAmountIRR = 900000) AS CommissionCount,
                    (SELECT COUNT(*) FROM dbo.Refunds r JOIN dbo.Payments p ON p.Id = r.PaymentId AND p.OrderId = r.OrderId WHERE r.Id = 78001 AND r.Status = 4) AS CompletedRefundCount,
                    (SELECT COUNT(*) FROM dbo.CommissionReversals cr JOIN dbo.Refunds r ON r.Id = cr.RefundId JOIN dbo.Commissions c ON c.Id = cr.CommissionId WHERE cr.OrderId = 75001 AND r.OrderId = 75001 AND c.OrderId = 75001) AS CommissionReversalCount,
                    (SELECT COUNT(*) FROM dbo.BalanceTransactions bt WHERE bt.OrderId = 75001 OR bt.RefundId = 78001 OR bt.SettlementId = 81001) AS LedgerCount,
                    (SELECT COUNT(*) FROM dbo.BalanceTransactions bt WHERE bt.SettlementId = 81001 AND bt.OrderId IS NULL) AS PooledSettlementLedgerCount,
                    (SELECT COUNT(*) FROM dbo.SellerBalances b WHERE b.SellerId = 72001) AS SellerBalanceCount;
                """, connection))
            await using (var reader = await trace.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0)); // order
                Assert.Equal(1, reader.GetInt32(1)); // payment
                Assert.Equal(1, reader.GetInt32(2)); // provider transaction
                Assert.Equal(1, reader.GetInt32(3)); // commission
                Assert.Equal(1, reader.GetInt32(4)); // completed refund
                Assert.Equal(1, reader.GetInt32(5)); // reversal
                Assert.Equal(4, reader.GetInt32(6)); // sale + refund + settlement + commission reversal
                Assert.Equal(1, reader.GetInt32(7)); // settlement is explicitly linked only through SettlementId
                Assert.Equal(1, reader.GetInt32(8)); // seller balance
            }

            // A completed refund without its own refund-linked ledger row must be detectable.
            await ExecuteAsync(connection, "DELETE dbo.BalanceTransactions WHERE Id = 82002;");
            await using (var missingRefundLedger = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.Refunds r
                WHERE r.Id = 78001
                  AND r.Status = 4
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.BalanceTransactions bt
                      WHERE bt.Type = 3
                        AND (bt.RefundId = r.Id OR (bt.RefundId IS NULL AND bt.OrderId = r.OrderId))
                  );
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await missingRefundLedger.ExecuteScalarAsync()));
            }

            // The schema must reject duplicate commission reversal for the same commission/refund.
            var duplicateRejected = false;
            try
            {
                await ExecuteAsync(connection, """
                    INSERT dbo.CommissionReversals
                        (Id, CommissionId, OrderId, RefundId, RefundAmountIRR, ReversedCommissionIRR, CreatedAtUtc)
                    VALUES (79002, 77001, 75001, 78001, 1000000, 100000, SYSUTCDATETIME());
                    """);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                duplicateRejected = true;
            }

            Assert.True(duplicateRejected, "The unique commission/refund reversal constraint should reject duplicate financial compensation.");
        }
        finally
        {
            await using var master = new SqlConnection(masterBuilder.ConnectionString);
            await master.OpenAsync();
            await ExecuteAsync(master, $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END;
                """);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
