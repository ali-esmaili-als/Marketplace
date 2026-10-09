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
    public void Ef_long_key_generation_matches_application_generated_id_contract()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;User ID=sa;Password=NotARealPassword123!;TrustServerCertificate=True")
            .Options;

        using var db = new MarketplaceDbContext(options);
        var identityAuditTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "PaymentReconciliationAudit",
            "RefundReconciliationAudit",
            "SettlementReconciliationAudit"
        };

        var mismatches = db.Model.GetEntityTypes()
            .Select(entity => new
            {
                Name = entity.ClrType.Name,
                Id = entity.FindProperty("Id")
            })
            .Where(x => x.Id?.ClrType == typeof(long))
            .Where(x => identityAuditTypes.Contains(x.Name)
                ? x.Id!.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd
                : x.Id!.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            .Select(x => $"{x.Name}.Id => {x.Id!.ValueGenerated}")
            .ToArray();

        Assert.True(mismatches.Length == 0,
            "Unexpected EF key-generation configuration: " + string.Join(", ", mismatches));
    }

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

            // Compile and execute every read-only operational diagnostic against a freshly
            // bootstrapped SQL Server schema. This catches drift in table/column names and
            // invalid T-SQL as the diagnostic suite grows.
            var diagnosticsPath = Path.Combine(AppContext.BaseDirectory, "database", "FinancialConsistencyChecks.sql");
            Assert.True(File.Exists(diagnosticsPath), $"Financial diagnostics SQL was not copied to test output: {diagnosticsPath}");
            var diagnostics = await File.ReadAllTextAsync(diagnosticsPath);
            await using (var runDiagnostics = new SqlCommand(diagnostics, connection) { CommandTimeout = 120 })
            {
                await runDiagnostics.ExecuteNonQueryAsync();
            }

            // Exercise the upgrade patch against a pre-existing schema state, then ensure
            // reapplying it is safe. The bootstrap script created the same index, so drop it
            // to simulate an installation that has not yet received patch 011.
            await using (var dropComplaintIndex = new SqlCommand("""
                DROP INDEX UX_Complaints_OneActivePerOrder ON dbo.Complaints;
                """, connection))
            {
                await dropComplaintIndex.ExecuteNonQueryAsync();
            }

            var complaintPatchPath = Path.Combine(AppContext.BaseDirectory, "database", "011_ActiveComplaintUniqueness.sql");
            Assert.True(File.Exists(complaintPatchPath), $"Complaint uniqueness patch was not copied to test output: {complaintPatchPath}");
            var complaintPatch = await File.ReadAllTextAsync(complaintPatchPath);
            await using (var applyComplaintPatch = new SqlCommand(complaintPatch, connection) { CommandTimeout = 120 })
            {
                await applyComplaintPatch.ExecuteNonQueryAsync();
            }

            await using (var verifyComplaintIndex = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.Complaints')
                  AND name = N'UX_Complaints_OneActivePerOrder'
                  AND is_unique = 1
                  AND has_filter = 1
                  AND filter_definition LIKE N'%Status%1%2%';
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyComplaintIndex.ExecuteScalarAsync()));
            }

            // The patch is intentionally idempotent for deployments that need safe retries.
            await using (var reapplyComplaintPatch = new SqlCommand(complaintPatch, connection) { CommandTimeout = 120 })
            {
                await reapplyComplaintPatch.ExecuteNonQueryAsync();
            }

            // Verify the complaint status upgrade patch recreates its check constraint
            // and can be safely rerun on an already-upgraded installation.
            await using (var dropComplaintStatusCheck = new SqlCommand("""
                ALTER TABLE dbo.Complaints DROP CONSTRAINT CK_Complaints_Status;
                """, connection))
            {
                await dropComplaintStatusCheck.ExecuteNonQueryAsync();
            }

            var complaintStatusPatchPath = Path.Combine(AppContext.BaseDirectory, "database", "012_ComplaintStatusConstraint.sql");
            Assert.True(File.Exists(complaintStatusPatchPath), $"Complaint status patch was not copied to test output: {complaintStatusPatchPath}");
            var complaintStatusPatch = await File.ReadAllTextAsync(complaintStatusPatchPath);
            await using (var applyComplaintStatusPatch = new SqlCommand(complaintStatusPatch, connection) { CommandTimeout = 120 })
            {
                await applyComplaintStatusPatch.ExecuteNonQueryAsync();
            }

            await using (var verifyComplaintStatusCheck = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.Complaints')
                  AND name = N'CK_Complaints_Status'
                  AND is_disabled = 0
                  AND is_not_trusted = 0;
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyComplaintStatusCheck.ExecuteScalarAsync()));
            }

            await using (var reapplyComplaintStatusPatch = new SqlCommand(complaintStatusPatch, connection) { CommandTimeout = 120 })
            {
                await reapplyComplaintStatusPatch.ExecuteNonQueryAsync();
            }

            // Simulate an existing database that has not received patch 013.
            await using (var dropLifecycleChecks = new SqlCommand("""
                ALTER TABLE dbo.SellerBalanceHolds DROP CONSTRAINT CK_SellerBalanceHolds_Status;
                ALTER TABLE dbo.InventoryReservations DROP CONSTRAINT CK_InventoryReservations_Status;
                ALTER TABLE dbo.Deliveries DROP CONSTRAINT CK_Deliveries_Status;
                ALTER TABLE dbo.BalanceTransactions DROP CONSTRAINT CK_BalanceTransactions_Type;
                ALTER TABLE dbo.BalanceTransactions DROP CONSTRAINT CK_BalanceTransactions_Bucket;
                """, connection))
            {
                await dropLifecycleChecks.ExecuteNonQueryAsync();
            }

            var lifecyclePatchPath = Path.Combine(AppContext.BaseDirectory, "database", "013_FinancialLifecycleStatusConstraints.sql");
            Assert.True(File.Exists(lifecyclePatchPath), $"Lifecycle status patch was not copied to test output: {lifecyclePatchPath}");
            var lifecyclePatch = await File.ReadAllTextAsync(lifecyclePatchPath);
            await using (var applyLifecyclePatch = new SqlCommand(lifecyclePatch, connection) { CommandTimeout = 120 })
            {
                await applyLifecyclePatch.ExecuteNonQueryAsync();
            }

            await using (var verifyLifecycleChecks = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.check_constraints
                WHERE name IN
                (
                    N'CK_SellerBalanceHolds_Status',
                    N'CK_InventoryReservations_Status',
                    N'CK_Deliveries_Status',
                    N'CK_BalanceTransactions_Type',
                    N'CK_BalanceTransactions_Bucket'
                )
                AND is_disabled = 0 AND is_not_trusted = 0;
                """, connection))
            {
                Assert.Equal(5, Convert.ToInt32(await verifyLifecycleChecks.ExecuteScalarAsync()));
            }

            // A rerun must not fail or create duplicate constraints.
            await using (var reapplyLifecyclePatch = new SqlCommand(lifecyclePatch, connection) { CommandTimeout = 120 })
            {
                await reapplyLifecyclePatch.ExecuteNonQueryAsync();
            }

            await using (var invalidBalanceTransactionType = new SqlCommand("""
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, SettlementId, Type, Bucket, AmountIRR,
                     BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES (949999, 910001, NULL, NULL, 99, 1, 0, 0, 0, N'INVALID-TYPE', SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => invalidBalanceTransactionType.ExecuteNonQueryAsync());
            }

            await using (var invalidBalanceBucket = new SqlCommand("""
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, SettlementId, Type, Bucket, AmountIRR,
                     BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES (949998, 910001, NULL, NULL, 5, 99, 0, 0, 0, N'INVALID-BUCKET', SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => invalidBalanceBucket.ExecuteNonQueryAsync());
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
                    SELECT N'CK_Complaints_Status', N'CHECK' UNION ALL
                    SELECT N'CK_Orders_Amounts', N'CHECK' UNION ALL
                    SELECT N'CK_Payments_Amount', N'CHECK' UNION ALL
                    SELECT N'CK_Refunds_Amount', N'CHECK' UNION ALL
                    SELECT N'CK_InventoryItems_Qty', N'CHECK' UNION ALL
                    SELECT N'CK_SellerBalances_NonNegative', N'CHECK' UNION ALL
                    SELECT N'CK_Settlements_Amount', N'CHECK' UNION ALL
                    SELECT N'UX_PaymentTransactions_Provider_Authority', N'INDEX' UNION ALL
                    SELECT N'UX_Refunds_OneActivePerOrder', N'INDEX' UNION ALL
                    SELECT N'UX_Complaints_OneActivePerOrder', N'INDEX' UNION ALL
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
                Assert.Equal(13, Convert.ToInt32(await contractCheck.ExecuteScalarAsync()));
            }

            await using (var fkCheck = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.foreign_keys
                WHERE name IN
                (
                    N'FK_Orders_Customers',
                    N'FK_OrderItems_Orders',
                    N'FK_Payments_Orders',
                    N'FK_Refunds_Orders',
                    N'FK_Settlements_Sellers'
                );
                """, connection))
            {
                Assert.Equal(5, Convert.ToInt32(await fkCheck.ExecuteScalarAsync()));
            }

            // Exercise the filtered unique indexes against the real bootstrap schema.
            // These are the final database guardrails against duplicate financial side effects.
            await using (var seedFinancialRows = new SqlCommand("""
                INSERT INTO dbo.Users (Id, Mobile, PasswordHash, DisplayName, CreatedAtUtc)
                VALUES (950001, N'+989120000001', N'test-hash', N'Integration Customer', SYSUTCDATETIME());
                INSERT INTO dbo.Sellers (Id, UserId, Status, CreatedAtUtc)
                VALUES (950002, 950001, 1, SYSUTCDATETIME());
                INSERT INTO dbo.Stores (Id, SellerId, Name, Slug, Status, CreatedAtUtc)
                VALUES (950003, 950002, N'Integration Store', N'integration-store', 1, SYSUTCDATETIME());
                INSERT INTO dbo.Orders
                    (Id, CustomerId, SellerId, StoreId, SubtotalAmountIRR, TotalAmountIRR, SellerAmountIRR, Status, CreatedAtUtc)
                VALUES
                    (950004, 950001, 950002, 950003, 100000, 100000, 90000, 1, SYSUTCDATETIME()),
                    (950005, 950001, 950002, 950003, 120000, 120000, 108000, 1, SYSUTCDATETIME());
                INSERT INTO dbo.Payments (Id, OrderId, CustomerId, AmountIRR, Status, CreatedAtUtc)
                VALUES
                    (950006, 950004, 950001, 100000, 1, SYSUTCDATETIME()),
                    (950007, 950005, 950001, 120000, 1, SYSUTCDATETIME());
                """, connection))
            {
                await seedFinancialRows.ExecuteNonQueryAsync();
            }

                        await using (var seedFirstRefund = new SqlCommand("""
                INSERT INTO dbo.Refunds (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
                VALUES (950008, 950004, 950006, 950001, 10000, 1, 1, SYSUTCDATETIME());
                """, connection))
            {
                await seedFirstRefund.ExecuteNonQueryAsync();
            }

            await using (var duplicateActiveRefund = new SqlCommand("""
                INSERT INTO dbo.Refunds (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
                VALUES (950009, 950004, 950006, 950001, 5000, 1, 2, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => duplicateActiveRefund.ExecuteNonQueryAsync());
            }

            // A definitive failed refund releases the filtered unique index slot so a
            // customer can retry after reconciliation confirms that no transfer occurred.
            await using (var failFirstRefund = new SqlCommand("""
                UPDATE dbo.Refunds SET Status = 5, FailureReason = N'Provider confirmed not refunded'
                WHERE Id = 950008;
                """, connection))
            {
                await failFirstRefund.ExecuteNonQueryAsync();
            }

            await using (var verifyFailedRefund = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.Refunds
                WHERE Id = 950008 AND Status = 5
                  AND FailureReason = N'Provider confirmed not refunded';
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyFailedRefund.ExecuteScalarAsync()));
            }

            await using (var retryRefund = new SqlCommand("""
                INSERT INTO dbo.Refunds (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
                VALUES (950021, 950004, 950006, 950001, 10000, 1, 1, SYSUTCDATETIME());
                """, connection))
            {
                await retryRefund.ExecuteNonQueryAsync();
            }

            await using (var verifyOneActiveRefund = new SqlCommand("""
                SELECT COUNT_BIG(*)
                FROM dbo.Refunds
                WHERE OrderId = 950004 AND Status IN (1, 2, 3);
                """, connection))
            {
                Assert.Equal(1L, Convert.ToInt64(await verifyOneActiveRefund.ExecuteScalarAsync()));
            }

            // A filtered unique index closes the race between concurrent complaint submissions.
            // Once a complaint is resolved, its active slot is released.
            await using (var seedActiveComplaint = new SqlCommand("""
                INSERT INTO dbo.Complaints
                    (Id, OrderId, CustomerId, SellerId, Status, Reason, CreatedAtUtc)
                VALUES (950040, 950004, 950001, 950002, 1, N'Integration complaint', SYSUTCDATETIME());
                """, connection))
            {
                await seedActiveComplaint.ExecuteNonQueryAsync();
            }

            await using (var duplicateActiveComplaint = new SqlCommand("""
                INSERT INTO dbo.Complaints
                    (Id, OrderId, CustomerId, SellerId, Status, Reason, CreatedAtUtc)
                VALUES (950041, 950004, 950001, 950002, 2, N'Duplicate active complaint', SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => duplicateActiveComplaint.ExecuteNonQueryAsync());
            }

            await using (var resolveFirstComplaint = new SqlCommand("""
                UPDATE dbo.Complaints
                SET Status = 3, ResolutionNote = N'Resolved for integration test', ResolvedAtUtc = SYSUTCDATETIME()
                WHERE Id = 950040;
                """, connection))
            {
                await resolveFirstComplaint.ExecuteNonQueryAsync();
            }

            await using (var openNextComplaint = new SqlCommand("""
                INSERT INTO dbo.Complaints
                    (Id, OrderId, CustomerId, SellerId, Status, Reason, CreatedAtUtc)
                VALUES (950042, 950004, 950001, 950002, 1, N'Next active complaint', SYSUTCDATETIME());
                """, connection))
            {
                await openNextComplaint.ExecuteNonQueryAsync();
            }

            await using (var verifyActiveComplaintSlot = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.Complaints
                WHERE OrderId = 950004 AND Status IN (1, 2);
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyActiveComplaintSlot.ExecuteScalarAsync()));
            }

            await using (var invalidComplaintStatus = new SqlCommand("""
                INSERT INTO dbo.Complaints
                    (Id, OrderId, CustomerId, SellerId, Status, Reason, CreatedAtUtc)
                VALUES (950043, 950004, 950001, 950002, 7, N'Invalid status must be rejected', SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => invalidComplaintStatus.ExecuteNonQueryAsync());
            }

            await using (var seedPaymentTransaction = new SqlCommand("""
                INSERT INTO dbo.PaymentTransactions (Id, PaymentId, AmountIRR, Status, Provider, Authority, CreatedAtUtc)
                VALUES (950010, 950006, 100000, 1, N'IntegrationGateway', N'same-authority', SYSUTCDATETIME());
                """, connection))
            {
                await seedPaymentTransaction.ExecuteNonQueryAsync();
            }

            await using (var duplicateAuthority = new SqlCommand("""
                INSERT INTO dbo.PaymentTransactions (Id, PaymentId, AmountIRR, Status, Provider, Authority, CreatedAtUtc)
                VALUES (950011, 950007, 120000, 1, N'IntegrationGateway', N'same-authority', SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => duplicateAuthority.ExecuteNonQueryAsync());
            }

            await using (var seedHold = new SqlCommand("""
                INSERT INTO dbo.SellerBalanceHolds (Id, SellerId, OrderId, AmountIRR, Reason, Status, CreatedAtUtc)
                VALUES (950012, 950002, 950004, 90000, N'Integration test hold', 1, SYSUTCDATETIME());
                """, connection))
            {
                await seedHold.ExecuteNonQueryAsync();
            }

            await using (var duplicateHold = new SqlCommand("""
                INSERT INTO dbo.SellerBalanceHolds (Id, SellerId, OrderId, AmountIRR, Reason, Status, CreatedAtUtc)
                VALUES (950013, 950002, 950004, 90000, N'Duplicate integration test hold', 1, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => duplicateHold.ExecuteNonQueryAsync());
            }

            await using (var seedSaleLedger = new SqlCommand("""
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, CreatedAtUtc)
                VALUES (950014, 950002, 950004, 1, 1, 90000, 0, 90000, SYSUTCDATETIME());
                """, connection))
            {
                await seedSaleLedger.ExecuteNonQueryAsync();
            }

            await using (var duplicateSaleLedger = new SqlCommand("""
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, CreatedAtUtc)
                VALUES (950015, 950002, 950004, 1, 1, 90000, 90000, 180000, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => duplicateSaleLedger.ExecuteNonQueryAsync());
            }

            // Race independent SQL connections against each filtered unique index. A sequential
            // duplicate insert only proves the constraint exists; this also exercises competing writers.
            async Task<bool[]> RaceTwoInsertsAsync(string firstSql, string secondSql)
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                async Task<bool> AttemptAsync(string sql)
                {
                    await using var contender = new SqlConnection(targetBuilder.ConnectionString);
                    await contender.OpenAsync();
                    await start.Task;
                    try
                    {
                        await using var command = new SqlCommand(sql, contender);
                        await command.ExecuteNonQueryAsync();
                        return true;
                    }
                    catch (SqlException)
                    {
                        return false;
                    }
                }

                var first = AttemptAsync(firstSql);
                var second = AttemptAsync(secondSql);
                start.SetResult();
                return await Task.WhenAll(first, second);
            }

            var refundRace = await RaceTwoInsertsAsync(
                """
                INSERT INTO dbo.Refunds (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
                VALUES (950017, 950005, 950007, 950001, 10000, 1, 1, SYSUTCDATETIME());
                """,
                """
                INSERT INTO dbo.Refunds (Id, OrderId, PaymentId, CustomerId, AmountIRR, Reason, Status, RequestedAtUtc)
                VALUES (950018, 950005, 950007, 950001, 12000, 1, 2, SYSUTCDATETIME());
                """);
            Assert.Equal(1, refundRace.Count(succeeded => succeeded));

            var authorityRace = await RaceTwoInsertsAsync(
                """
                INSERT INTO dbo.PaymentTransactions (Id, PaymentId, AmountIRR, Status, Provider, Authority, CreatedAtUtc)
                VALUES (950019, 950006, 100000, 1, N'ConcurrentGateway', N'concurrent-authority', SYSUTCDATETIME());
                """,
                """
                INSERT INTO dbo.PaymentTransactions (Id, PaymentId, AmountIRR, Status, Provider, Authority, CreatedAtUtc)
                VALUES (950020, 950007, 120000, 1, N'ConcurrentGateway', N'concurrent-authority', SYSUTCDATETIME());
                """);
            Assert.Equal(1, authorityRace.Count(succeeded => succeeded));

            var holdRace = await RaceTwoInsertsAsync(
                """
                INSERT INTO dbo.SellerBalanceHolds (Id, SellerId, OrderId, AmountIRR, Reason, Status, CreatedAtUtc)
                VALUES (950021, 950002, 950005, 108000, N'Concurrent hold A', 1, SYSUTCDATETIME());
                """,
                """
                INSERT INTO dbo.SellerBalanceHolds (Id, SellerId, OrderId, AmountIRR, Reason, Status, CreatedAtUtc)
                VALUES (950022, 950002, 950005, 108000, N'Concurrent hold B', 1, SYSUTCDATETIME());
                """);
            Assert.Equal(1, holdRace.Count(succeeded => succeeded));

            var saleLedgerRace = await RaceTwoInsertsAsync(
                """
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, CreatedAtUtc)
                VALUES (950023, 950002, 950005, 1, 1, 108000, 0, 108000, SYSUTCDATETIME());
                """,
                """
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, CreatedAtUtc)
                VALUES (950024, 950002, 950005, 1, 1, 108000, 0, 108000, SYSUTCDATETIME());
                """);
            Assert.Equal(1, saleLedgerRace.Count(succeeded => succeeded));

            // Two independent sessions compete to reserve more seller funds than are available.
            // The conditional UPDATE is the database-level last line of defense against over-reserving.
            await using (var seedSellerBalance = new SqlCommand("""
                INSERT INTO dbo.SellerBalances
                    (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
                VALUES (950025, 950002, 100000, 0, 0, 0, 0, SYSUTCDATETIME());
                """, connection))
            {
                await seedSellerBalance.ExecuteNonQueryAsync();
            }

            // Exercise the new operational diagnostics against deliberate inconsistencies.
            // The SQL is read-only: these fixtures prove the checks surface a mismatch, not repair it.
            await using (var seedBalanceAndSaleLedger = new SqlCommand("""
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, OrderId, SettlementId, Type, Bucket, AmountIRR,
                     BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES (950040, 950002, 950004, NULL, 5, 1, 90000, 0, 90000, N'INTEGRATION-ADJUSTMENT', SYSUTCDATETIME());
                """, connection))
            {
                await seedBalanceAndSaleLedger.ExecuteNonQueryAsync();
            }

            await using (var latestLedgerMismatch = new SqlCommand("""
                SELECT COUNT_BIG(*)
                FROM dbo.SellerBalances AS sb
                CROSS APPLY (VALUES
                    (1, sb.AvailableIRR),
                    (2, sb.PendingIRR),
                    (3, sb.BlockedIRR),
                    (4, sb.ReservedForSettlementIRR),
                    (5, sb.LiabilityIRR)
                ) AS b(Bucket, BalanceIRR)
                OUTER APPLY
                (
                    SELECT TOP (1) bt.BalanceAfterIRR
                    FROM dbo.BalanceTransactions AS bt
                    WHERE bt.SellerId = sb.SellerId AND bt.Bucket = b.Bucket
                    ORDER BY bt.CreatedAtUtc DESC, bt.Id DESC
                ) AS latest
                WHERE sb.SellerId = 950002 AND b.Bucket = 1
                  AND latest.BalanceAfterIRR <> b.BalanceIRR;
                """, connection))
            {
                Assert.Equal(1L, Convert.ToInt64(await latestLedgerMismatch.ExecuteScalarAsync()));
            }


            var reservationStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<int> TryReserveSellerFundsAsync()
            {
                await using var contender = new SqlConnection(targetBuilder.ConnectionString);
                await contender.OpenAsync();
                await reservationStart.Task;
                await using var command = new SqlCommand("""
                    UPDATE dbo.SellerBalances
                    SET ReservedForSettlementIRR = ReservedForSettlementIRR + 70000,
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE SellerId = 950002
                      AND AvailableIRR - ReservedForSettlementIRR >= 70000;
                    """, contender);
                return await command.ExecuteNonQueryAsync();
            }

            var firstReservation = TryReserveSellerFundsAsync();
            var secondReservation = TryReserveSellerFundsAsync();
            reservationStart.SetResult();
            var reservationRows = await Task.WhenAll(firstReservation, secondReservation);
            Assert.Equal(1, reservationRows.Sum());

            await using (var verifyReservedFunds = new SqlCommand("""
                SELECT ReservedForSettlementIRR
                FROM dbo.SellerBalances
                WHERE SellerId = 950002;
                """, connection))
            {
                Assert.Equal(70000L, Convert.ToInt64(await verifyReservedFunds.ExecuteScalarAsync()));
            }

            // Exercise both terminal settlement outcomes against the actual SQL Server schema.
            // These rows intentionally mirror the application ledger contract and are then
            // checked with the same cross-table invariants used by operational diagnostics.
            await using (var seedSuccessfulSettlement = new SqlCommand("""
                INSERT INTO dbo.SellerBankAccounts
                    (Id, SellerId, BankName, Iban, AccountHolderName, IsDefault, IsVerified, CreatedAtUtc)
                VALUES (950026, 950002, N'Integration Bank', N'IR0000000000000000000000000001', N'Integration Seller', 1, 1, SYSUTCDATETIME());

                INSERT INTO dbo.Settlements
                    (Id, SellerId, AmountIRR, Status, BankAccountId, BankNameSnapshot, IbanSnapshot,
                     AccountHolderNameSnapshot, RequestedAtUtc)
                VALUES
                    (950027, 950002, 70000, 1, 950026, N'Integration Bank',
                     N'IR0000000000000000000000000001', N'Integration Seller', SYSUTCDATETIME());

                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, SettlementId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES
                    (950028, 950002, 950027, 4, 4, 70000, 0, 70000, N'reservation', SYSUTCDATETIME());

                UPDATE dbo.SellerBalances
                SET AvailableIRR = 30000, ReservedForSettlementIRR = 0, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE SellerId = 950002;

                UPDATE dbo.Settlements
                SET Status = 3, Reference = N'integration-bank-reference', CompletedAtUtc = SYSUTCDATETIME()
                WHERE Id = 950027;

                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, SettlementId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES
                    (950029, 950002, 950027, 4, 1, 70000, 100000, 30000, N'integration-bank-reference', SYSUTCDATETIME());
                """, connection))
            {
                await seedSuccessfulSettlement.ExecuteNonQueryAsync();
            }

            await using (var verifySuccessfulSettlement = new SqlCommand("""
                SELECT
                    CASE WHEN s.Status = 3
                              AND b.AvailableIRR = 30000
                              AND b.ReservedForSettlementIRR = 0
                              AND EXISTS
                                  (SELECT 1 FROM dbo.BalanceTransactions bt
                                   WHERE bt.SettlementId = s.Id AND bt.SellerId = s.SellerId
                                     AND bt.Type = 4 AND bt.Bucket = 1
                                     AND bt.AmountIRR = s.AmountIRR
                                     AND bt.BalanceBeforeIRR = 100000 AND bt.BalanceAfterIRR = 30000)
                              AND (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions bt
                                   WHERE bt.SettlementId = s.Id AND bt.Type = 4 AND bt.Bucket = 1) = 1
                         THEN 1 ELSE 0 END
                FROM dbo.Settlements s
                INNER JOIN dbo.SellerBalances b ON b.SellerId = s.SellerId
                WHERE s.Id = 950027;
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifySuccessfulSettlement.ExecuteScalarAsync()));
            }

            await using (var seedFailedSettlement = new SqlCommand("""
                INSERT INTO dbo.Users (Id, Mobile, PasswordHash, DisplayName, CreatedAtUtc)
                VALUES (950030, N'+989120000002', N'test-hash', N'Integration Seller Two', SYSUTCDATETIME());
                INSERT INTO dbo.Sellers (Id, UserId, Status, CreatedAtUtc)
                VALUES (950031, 950030, 1, SYSUTCDATETIME());
                INSERT INTO dbo.SellerBankAccounts
                    (Id, SellerId, BankName, Iban, AccountHolderName, IsDefault, IsVerified, CreatedAtUtc)
                VALUES (950032, 950031, N'Integration Bank', N'IR0000000000000000000000000002', N'Integration Seller Two', 1, 1, SYSUTCDATETIME());
                INSERT INTO dbo.SellerBalances
                    (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
                VALUES (950033, 950031, 100000, 0, 0, 25000, 0, SYSUTCDATETIME());
                INSERT INTO dbo.Settlements
                    (Id, SellerId, AmountIRR, Status, BankAccountId, BankNameSnapshot, IbanSnapshot,
                     AccountHolderNameSnapshot, RequestedAtUtc)
                VALUES
                    (950034, 950031, 25000, 1, 950032, N'Integration Bank',
                     N'IR0000000000000000000000000002', N'Integration Seller Two', SYSUTCDATETIME());
                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, SettlementId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES
                    (950035, 950031, 950034, 4, 4, 25000, 0, 25000, N'reservation', SYSUTCDATETIME());

                UPDATE dbo.SellerBalances
                SET ReservedForSettlementIRR = 0, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE SellerId = 950031;

                UPDATE dbo.Settlements
                SET Status = 4, FailureReason = N'definitive bank rejection', CompletedAtUtc = SYSUTCDATETIME()
                WHERE Id = 950034;

                INSERT INTO dbo.BalanceTransactions
                    (Id, SellerId, SettlementId, Type, Bucket, AmountIRR, BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES
                    (950036, 950031, 950034, 14, 4, 25000, 25000, 0, N'definitive bank rejection', SYSUTCDATETIME());
                """, connection))
            {
                await seedFailedSettlement.ExecuteNonQueryAsync();
            }

            await using (var verifyFailedSettlement = new SqlCommand("""
                SELECT
                    CASE WHEN s.Status = 4
                              AND b.AvailableIRR = 100000
                              AND b.ReservedForSettlementIRR = 0
                              AND EXISTS
                                  (SELECT 1 FROM dbo.BalanceTransactions bt
                                   WHERE bt.SettlementId = s.Id AND bt.SellerId = s.SellerId
                                     AND bt.Type = 14 AND bt.Bucket = 4
                                     AND bt.AmountIRR = s.AmountIRR
                                     AND bt.BalanceBeforeIRR = 25000 AND bt.BalanceAfterIRR = 0)
                              AND NOT EXISTS
                                  (SELECT 1 FROM dbo.BalanceTransactions bt
                                   WHERE bt.SettlementId = s.Id AND bt.Type = 4 AND bt.Bucket = 1)
                         THEN 1 ELSE 0 END
                FROM dbo.Settlements s
                INNER JOIN dbo.SellerBalances b ON b.SellerId = s.SellerId
                WHERE s.Id = 950034;
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyFailedSettlement.ExecuteScalarAsync()));
            }

            // Reconciliation invariants: active settlements must equal reserved funds,
            // and terminal settlements must not be missing their matching final ledger record.
            await using (var verifyNoSettlementDrift = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.Settlements s
                LEFT JOIN dbo.SellerBalances b ON b.SellerId = s.SellerId
                WHERE s.Id IN (950027, 950034)
                  AND
                  (
                      b.SellerId IS NULL
                      OR (s.Status = 3 AND NOT EXISTS
                          (SELECT 1 FROM dbo.BalanceTransactions bt
                           WHERE bt.SettlementId = s.Id AND bt.SellerId = s.SellerId
                             AND bt.Type = 4 AND bt.Bucket = 1 AND bt.AmountIRR = s.AmountIRR))
                      OR (s.Status = 4 AND NOT EXISTS
                          (SELECT 1 FROM dbo.BalanceTransactions bt
                           WHERE bt.SettlementId = s.Id AND bt.SellerId = s.SellerId
                             AND bt.Type = 14 AND bt.AmountIRR = s.AmountIRR))
                  );
                """, connection))
            {
                Assert.Equal(0, Convert.ToInt32(await verifyNoSettlementDrift.ExecuteScalarAsync()));
            }

            await using (var identityCheck = new SqlCommand("""
                SELECT COUNT(*)
                FROM sys.identity_columns ic
                INNER JOIN sys.tables t ON t.object_id = ic.object_id
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = N'dbo'
                  AND t.name IN
                  (
                    N'PaymentReconciliationAudits',
                    N'RefundReconciliationAudits',
                    N'SettlementReconciliationAudits'
                  );
                """, connection))
            {
                Assert.Equal(3, Convert.ToInt32(await identityCheck.ExecuteScalarAsync()));
            }

            // The SQL schema must reject incomplete refund reconciliation evidence,
            // while accepting a complete audit row linked to the refund.
            await using (var seedValidRefundAudit = new SqlCommand("""
                INSERT INTO dbo.RefundReconciliationAudits
                    (RefundId, AdminUserId, TransferCompleted, Note, BankReference, CreatedAtUtc)
                VALUES
                    (950008, 950001, 1, N'Confirmed with provider', N'BANK-REF-950008', SYSUTCDATETIME());
                """, connection))
            {
                await seedValidRefundAudit.ExecuteNonQueryAsync();
            }

            await using (var verifyRefundAudit = new SqlCommand("""
                SELECT COUNT(*)
                FROM dbo.RefundReconciliationAudits
                WHERE RefundId = 950008
                  AND TransferCompleted = 1
                  AND LEN(LTRIM(RTRIM(Note))) > 0
                  AND LEN(LTRIM(RTRIM(BankReference))) > 0;
                """, connection))
            {
                Assert.Equal(1, Convert.ToInt32(await verifyRefundAudit.ExecuteScalarAsync()));
            }

            await using (var missingBankReference = new SqlCommand("""
                INSERT INTO dbo.RefundReconciliationAudits
                    (RefundId, AdminUserId, TransferCompleted, Note, BankReference, CreatedAtUtc)
                VALUES
                    (950008, 950001, 1, N'Provider confirmed', NULL, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => missingBankReference.ExecuteNonQueryAsync());
            }

            await using (var blankAuditNote = new SqlCommand("""
                INSERT INTO dbo.RefundReconciliationAudits
                    (RefundId, AdminUserId, TransferCompleted, Note, BankReference, CreatedAtUtc)
                VALUES
                    (950008, 950001, 0, N'   ', NULL, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => blankAuditNote.ExecuteNonQueryAsync());
            }

            await using (var missingRefund = new SqlCommand("""
                INSERT INTO dbo.RefundReconciliationAudits
                    (RefundId, AdminUserId, TransferCompleted, Note, BankReference, CreatedAtUtc)
                VALUES
                    (999998, 950001, 0, N'No refund exists', NULL, SYSUTCDATETIME());
                """, connection))
            {
                await Assert.ThrowsAsync<SqlException>(() => missingRefund.ExecuteNonQueryAsync());
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
