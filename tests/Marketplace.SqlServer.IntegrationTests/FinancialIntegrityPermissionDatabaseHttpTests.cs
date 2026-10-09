using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Verifies that the production permission handler reads real UserRules/Rules rows and that
/// an authorized read-only order trace leaves seller balances and ledger rows unchanged.
/// </summary>
[Collection("FinancialIntegrityHttpTests")]
public sealed class FinancialIntegrityPermissionDatabaseHttpTests : IAsyncLifetime
{
    private const string DatabaseName = "MarketplaceFinancialIntegrityPermissionHttpTests";
    private const string JwtKey = "Marketplace-Test-Only-Signing-Key-Must-Be-At-Least-32-Characters";
    private const string JwtIssuer = "Marketplace.IntegrationTests";
    private const string JwtAudience = "Marketplace.IntegrationTests";
    private const long CustomerUserId = 71001;
    private const long AuthorizedUserId = 71002;
    private const long SellerId = 72001;
    private const long StoreId = 73001;
    private const long OrderId = 75001;
    private const long SellerBalanceId = 80001;
    private const long LedgerId = 82001;
    private const string PermissionCode = "Admin.Settlement.Process";

    private readonly string _baseConnectionString;
    private string _targetConnectionString = null!;
    private readonly Dictionary<string, string?> _originalEnvironment = new();
    private WebApplicationFactory<global::Program>? _factory;
    private HttpClient? _client;

    public FinancialIntegrityPermissionDatabaseHttpTests()
    {
        _baseConnectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER")
            ?? throw new InvalidOperationException("MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");
    }

    public async Task InitializeAsync()
    {
        var masterBuilder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, $"""
                IF DB_ID(N'{DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{DatabaseName}];
                END;
                CREATE DATABASE [{DatabaseName}];
                """);
        }

        var targetBuilder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = DatabaseName };
        _targetConnectionString = targetBuilder.ConnectionString;
        try
        {
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script was not copied to test output: {bootstrapPath}");
            await using var connection = new SqlConnection(_targetConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, await File.ReadAllTextAsync(bootstrapPath));
            await ExecuteAsync(connection, """
                DECLARE @now DATETIME2(7) = SYSUTCDATETIME();

                INSERT dbo.Users(Id, Mobile, PasswordHash, DisplayName, CreatedAtUtc)
                VALUES
                    (71001, N'09120007101', N'test-hash-customer', N'Permission Test Customer', @now),
                    (71002, N'09120007102', N'test-hash-authorized', N'Permission Test Admin', @now);

                -- The complete bootstrap schema seeds Admin.Settlement.Process as Rule 3003.
                INSERT dbo.UserRules(Id, UserId, RuleId, GrantedAtUtc)
                VALUES (99002, 71002, 3003, @now);

                INSERT dbo.Sellers(Id, UserId, Status, CommissionRateBasisPoints, MinimumCommissionIRR, MaxStoreCount, CreatedAtUtc, ActivatedAtUtc)
                VALUES (72001, 71002, 2, 1000, 0, 2, @now, @now);

                INSERT dbo.Stores(Id, SellerId, Name, Slug, Status, CommissionRateBasisPoints, MinimumCommissionIRR, CreatedAtUtc)
                VALUES (73001, 72001, N'Permission Test Store', N'permission-test-store', 2, 1000, 0, @now);

                INSERT dbo.Orders
                    (Id, CustomerId, SellerId, StoreId, SubtotalAmountIRR, CampaignDiscountIRR, CouponDiscountIRR,
                     TotalAmountIRR, SellerAmountIRR, Status, CreatedAtUtc, PaidAtUtc)
                VALUES (75001, 71001, 72001, 73001, 1000000, 0, 0, 1000000, 900000, 8, @now, @now);

                INSERT dbo.SellerBalances
                    (Id, SellerId, AvailableIRR, PendingIRR, BlockedIRR, ReservedForSettlementIRR, LiabilityIRR, UpdatedAtUtc)
                VALUES (80001, 72001, 900000, 0, 0, 0, 0, @now);

                INSERT dbo.BalanceTransactions
                    (Id, SellerId, OrderId, SettlementId, RefundId, Type, Bucket, AmountIRR,
                     BalanceBeforeIRR, BalanceAfterIRR, Reference, CreatedAtUtc)
                VALUES (82001, 72001, 75001, NULL, NULL, 1, 2, 900000, 0, 900000, N'SALE-PERMISSION-TEST', @now);

                INSERT dbo.OutboxMessages
                    (Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc, LockedUntilUtc, LockToken,
                     NextAttemptAtUtc, Attempts, Status, LastError)
                VALUES
                    (91001, '91001000-0000-0000-0000-000000000001', N'Settlement.Requested', N'{"settlementId":1}', @now, NULL, NULL, NULL, @now, 0, N'Pending', NULL),
                    (91002, '91002000-0000-0000-0000-000000000002', N'Settlement.Completed', N'{"settlementId":2}', @now, NULL, NULL, NULL, @now, 8, N'DeadLetter', N'Webhook unavailable');
                """);

            SetEnvironment("Authentication__Jwt__Key", JwtKey);
            SetEnvironment("Authentication__Jwt__Issuer", JwtIssuer);
            SetEnvironment("Authentication__Jwt__Audience", JwtAudience);
            SetEnvironment("ConnectionStrings__Marketplace", _targetConnectionString);

            _factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Authentication:Jwt:Key"] = JwtKey,
                        ["Authentication:Jwt:Issuer"] = JwtIssuer,
                        ["Authentication:Jwt:Audience"] = JwtAudience,
                        ["ConnectionStrings:Marketplace"] = _targetConnectionString
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    foreach (var descriptor in services
                        .Where(x => x.ImplementationType == typeof(global::MarketplaceMaintenanceHostedService))
                        .ToArray())
                        services.Remove(descriptor);
                });
            });
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }
        catch
        {
            await DropDatabaseAsync();
            throw;
        }
    }

    [Fact]
    public async Task Outbox_archive_moves_only_old_processed_rows_and_leaves_financial_state_unchanged()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        await using (var connection = new SqlConnection(_targetConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                INSERT dbo.OutboxMessages
                    (Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc, LockedUntilUtc, LockToken,
                     NextAttemptAtUtc, Attempts, Status, LastError)
                VALUES
                    (91004, '91004000-0000-0000-0000-000000000004', N'Settlement.Completed', N'{"settlementId":4}',
                     DATEADD(DAY,-120,SYSUTCDATETIME()), DATEADD(DAY,-120,SYSUTCDATETIME()), NULL, NULL,
                     DATEADD(DAY,-120,SYSUTCDATETIME()), 1, N'Processed', NULL),
                    (91005, '91005000-0000-0000-0000-000000000005', N'Settlement.Requested', N'{"settlementId":5}',
                     DATEADD(DAY,-120,SYSUTCDATETIME()), NULL, NULL, NULL,
                     DATEADD(DAY,-120,SYSUTCDATETIME()), 0, N'Pending', NULL);
                """);

            await using var archive = new SqlCommand("""
                SET XACT_ABORT ON;
                BEGIN TRY
                    BEGIN TRANSACTION;

                    DECLARE @Deleted TABLE
                    (
                        Id BIGINT NOT NULL,
                        MessageId UNIQUEIDENTIFIER NOT NULL,
                        EventType NVARCHAR(200) NOT NULL,
                        PayloadJson NVARCHAR(MAX) NOT NULL,
                        OccurredAtUtc DATETIME2(7) NOT NULL,
                        ProcessedAtUtc DATETIME2(7) NOT NULL,
                        LockedUntilUtc DATETIME2(7) NULL,
                        LockToken UNIQUEIDENTIFIER NULL,
                        NextAttemptAtUtc DATETIME2(7) NOT NULL,
                        Attempts INT NOT NULL,
                        Status NVARCHAR(20) NOT NULL,
                        LastError NVARCHAR(2000) NULL
                    );

                    ;WITH candidates AS
                    (
                        SELECT TOP (@BatchSize) *
                        FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE Status = N'Processed'
                          AND ProcessedAtUtc IS NOT NULL
                          AND ProcessedAtUtc < @CutoffUtc
                        ORDER BY ProcessedAtUtc, Id
                    )
                    DELETE FROM candidates
                    OUTPUT
                        deleted.Id, deleted.MessageId, deleted.EventType, deleted.PayloadJson,
                        deleted.OccurredAtUtc, deleted.ProcessedAtUtc, deleted.LockedUntilUtc,
                        deleted.LockToken, deleted.NextAttemptAtUtc, deleted.Attempts,
                        deleted.Status, deleted.LastError
                    INTO @Deleted;

                    INSERT dbo.OutboxMessageArchive
                        (Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc,
                         LockedUntilUtc, LockToken, NextAttemptAtUtc, Attempts, Status, LastError)
                    SELECT Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc,
                           LockedUntilUtc, LockToken, NextAttemptAtUtc, Attempts, Status, LastError
                    FROM @Deleted;

                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;
                """, connection);
            archive.Parameters.AddWithValue("@BatchSize", 100);
            archive.Parameters.AddWithValue("@CutoffUtc", cutoff);
            await archive.ExecuteNonQueryAsync();

            await using var verify = new SqlCommand("""
                SELECT
                    (SELECT COUNT_BIG(*) FROM dbo.OutboxMessageArchive WHERE Id=91004 AND Status=N'Processed'),
                    (SELECT COUNT_BIG(*) FROM dbo.OutboxMessages WHERE Id=91004),
                    (SELECT COUNT_BIG(*) FROM dbo.OutboxMessages WHERE Id=91005 AND Status=N'Pending'),
                    (SELECT COUNT_BIG(*) FROM dbo.OutboxMessages WHERE Id=91002 AND Status=N'DeadLetter'),
                    (SELECT AvailableIRR FROM dbo.SellerBalances WHERE SellerId=72001),
                    (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions WHERE SellerId=72001);
                """, connection);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.Equal(900000L, reader.GetInt64(4));
            Assert.Equal(1L, reader.GetInt64(5));
        }

        SetBearerToken(CustomerUserId);
        using var deniedArchive = await Client.GetAsync("/api/admin/outbox/archive");
        Assert.Equal(HttpStatusCode.Forbidden, deniedArchive.StatusCode);

        SetBearerToken(AuthorizedUserId);
        using var archiveList = await Client.GetAsync("/api/admin/outbox/archive?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, archiveList.StatusCode);
        var archiveJson = await archiveList.Content.ReadAsStringAsync();
        Assert.Contains("Settlement.Completed", archiveJson);
        Assert.Contains("\\"total\\":1", archiveJson, StringComparison.OrdinalIgnoreCase);

        using var archiveDetail = await Client.GetAsync("/api/admin/outbox/archive/91004");
        Assert.Equal(HttpStatusCode.OK, archiveDetail.StatusCode);
        var detailJson = await archiveDetail.Content.ReadAsStringAsync();
        Assert.Contains("settlementId", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ArchivedAtUtc", detailJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Permission_is_resolved_from_database_user_rule_mapping()
    {
        SetBearerToken(CustomerUserId);
        using var denied = await Client.GetAsync($"/api/admin/financial-integrity/order-trace/{OrderId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        SetBearerToken(AuthorizedUserId);
        using var allowed = await Client.GetAsync($"/api/admin/financial-integrity/order-trace/{OrderId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Authorized_read_only_order_trace_does_not_change_seller_balance_or_ledger()
    {
        SetBearerToken(AuthorizedUserId);
        var before = await ReadFinancialSnapshotAsync();

        using var response = await Client.GetAsync($"/api/admin/financial-integrity/order-trace/{OrderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ReadFinancialSnapshotAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Authorized_trace_returns_not_found_for_missing_order()
    {
        SetBearerToken(AuthorizedUserId);
        using var response = await Client.GetAsync("/api/admin/financial-integrity/order-trace/75999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Outbox_monitoring_requires_admin_permission_and_returns_summary_and_paged_messages()
    {
        SetBearerToken(CustomerUserId);
        using var denied = await Client.GetAsync("/api/admin/outbox/summary");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        SetBearerToken(AuthorizedUserId);
        using var summary = await Client.GetAsync("/api/admin/outbox/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var summaryJson = await summary.Content.ReadAsStringAsync();
        Assert.Contains("\"deadLetter\":1", summaryJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"pending\":1", summaryJson, StringComparison.OrdinalIgnoreCase);

        using var list = await Client.GetAsync("/api/admin/outbox/messages");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.Contains("\"total\":2", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Settlement.Requested", listJson);
    }

    [Fact]
    public async Task Outbox_health_detects_dead_letters_overdue_pending_and_expired_processing_leases()
    {
        await using (var connection = new SqlConnection(_targetConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                UPDATE dbo.OutboxMessages
                SET OccurredAtUtc = DATEADD(MINUTE, -45, SYSUTCDATETIME()),
                    NextAttemptAtUtc = DATEADD(MINUTE, -30, SYSUTCDATETIME())
                WHERE Id = 91001;

                INSERT dbo.OutboxMessages
                    (Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc, LockedUntilUtc, LockToken,
                     NextAttemptAtUtc, Attempts, Status, LastError)
                VALUES
                    (91003, '91003000-0000-0000-0000-000000000003', N'Settlement.Processing', N'{"settlementId":3}',
                     DATEADD(MINUTE, -40, SYSUTCDATETIME()), NULL, DATEADD(MINUTE, -10, SYSUTCDATETIME()),
                     '91003000-0000-0000-0000-000000000004', DATEADD(MINUTE, -40, SYSUTCDATETIME()), 2, N'Processing', NULL);
                """);
        }

        SetBearerToken(CustomerUserId);
        using var denied = await Client.GetAsync("/api/admin/outbox/health");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        SetBearerToken(AuthorizedUserId);
        using var response = await Client.GetAsync("/api/admin/outbox/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Critical\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Outbox.DeadLetter", json);
        Assert.Contains("Outbox.StaleProcessing", json);
        Assert.Contains("Outbox.PendingBacklog", json);
        Assert.Contains("Outbox.DispatcherDisabled", json);
        Assert.Contains("\"overduePendingCount\":1", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"staleProcessingCount\":1", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"deadLetterCount\":1", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dead_letter_retry_is_audited_and_resets_message_for_delivery()
    {
        SetBearerToken(AuthorizedUserId);
        using var response = await Client.PostAsync("/api/admin/outbox/messages/91002/retry", new StringContent("{}", Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Retry returned {(int)response.StatusCode}: {responseBody}");

        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT Status, Attempts, NextAttemptAtUtc, LockToken
            FROM dbo.OutboxMessages WHERE Id = 91002;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Pending", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.True(reader.GetDateTime(2) <= DateTime.UtcNow.AddSeconds(5));
        Assert.True(await reader.IsDBNullAsync(3));
        await reader.CloseAsync();

        await using var auditCommand = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AdminAuditEvents
            WHERE Action = N'Outbox.MessageRetried' AND EntityType = N'OutboxMessage' AND EntityKey = N'91002';
            """, connection);
        Assert.Equal(1, Convert.ToInt32(await auditCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Outbox_retry_rejects_non_dead_letter_and_missing_messages()
    {
        SetBearerToken(AuthorizedUserId);
        using var pending = await Client.PostAsync("/api/admin/outbox/messages/91001/retry", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);

        using var missing = await Client.PostAsync("/api/admin/outbox/messages/91999/retry", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("HTTP test client was not initialized.");

    private void SetBearerToken(long userId)
        => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));

    private static string CreateToken(long userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> ReadFinancialSnapshotAsync()
    {
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions WHERE SellerId = @sellerId) AS LedgerCount,
                (SELECT COALESCE(SUM(AmountIRR), 0) FROM dbo.BalanceTransactions WHERE SellerId = @sellerId) AS LedgerAmount,
                b.AvailableIRR, b.PendingIRR, b.BlockedIRR, b.ReservedForSettlementIRR,
                b.LiabilityIRR, b.UpdatedAtUtc
            FROM dbo.SellerBalances b
            WHERE b.Id = @balanceId AND b.SellerId = @sellerId;
            """, connection);
        command.Parameters.AddWithValue("@sellerId", SellerId);
        command.Parameters.AddWithValue("@balanceId", SellerBalanceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Seeded seller balance must exist.");
        return string.Join("|", Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.GetValue(index) is DateTime timestamp
                ? timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        await DropDatabaseAsync();
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private async Task DropDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
            return;
        var masterBuilder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterBuilder.ConnectionString);
        await master.OpenAsync();
        await ExecuteAsync(master, $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END;
            """);
    }
}
