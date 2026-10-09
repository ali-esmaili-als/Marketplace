using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Persistence;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>SQL Server race tests for the database invariants used by settlement requests.</summary>
[Collection("FinancialIntegrityHttpTests")]
public sealed class SettlementConcurrencyIntegrationTests
{
    [Fact]
    public async Task Concurrent_requests_with_same_key_create_only_one_reservation()
    {
        var (db, cs, master) = await CreateAsync();
        try
        {
            await SeedAsync(cs, 1_000_000);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "same", 600_000); });
            var b = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "same", 600_000); });
            start.SetResult();
            var results = await Task.WhenAll(a, b);
            Assert.Equal(results[0], results[1]);
            Assert.True(results[0].Accepted);

            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT (SELECT COUNT_BIG(*) FROM dbo.Settlements WHERE SellerId=72001 AND RequestKey=N'same'),
                       (SELECT AvailableIRR FROM dbo.SellerBalances WHERE SellerId=72001),
                       (SELECT ReservedForSettlementIRR FROM dbo.SellerBalances WHERE SellerId=72001),
                       (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions WHERE SellerId=72001 AND SettlementId IS NOT NULL);
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1_000_000L, reader.GetInt64(1));
            Assert.Equal(600_000L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
        }
        finally { await DropAsync(db, master); }
    }

    [Fact]
    public async Task Concurrent_different_keys_cannot_overdraw_withdrawable_balance()
    {
        var (db, cs, master) = await CreateAsync();
        try
        {
            await SeedAsync(cs, 1_000_000);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "key-a", 700_000); });
            var b = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "key-b", 700_000); });
            start.SetResult();
            var results = await Task.WhenAll(a, b);
            Assert.Single(results, x => x.Accepted);
            Assert.Single(results, x => !x.Accepted);

            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT COUNT_BIG(*), (SELECT AvailableIRR FROM dbo.SellerBalances WHERE SellerId=72001),
                       (SELECT ReservedForSettlementIRR FROM dbo.SellerBalances WHERE SellerId=72001)
                FROM dbo.Settlements WHERE SellerId=72001;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1_000_000L, reader.GetInt64(1));
            Assert.Equal(700_000L, reader.GetInt64(2));
        }
        finally { await DropAsync(db, master); }
    }

    [Fact]
    public async Task Concurrent_processing_claims_only_once_and_deducts_balance_once()
    {
        var (db, cs, master) = await CreateAsync();
        try
        {
            await SeedAsync(cs, 1_000_000);
            await using (var connection = new SqlConnection(cs))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("""
                    INSERT dbo.Settlements
                        (Id,SellerId,RequestKey,AmountIRR,Status,BankAccountId,BankNameSnapshot,IbanSnapshot,AccountHolderNameSnapshot,RequestedAtUtc)
                    VALUES (81001,72001,N'process-race',500000,1,74001,N'Test Bank',N'IR000000000000000000000000',N'Race Seller',SYSUTCDATETIME());
                    UPDATE dbo.SellerBalances SET ReservedForSettlementIRR=500000 WHERE SellerId=72001;
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }

            var gateway = new BlockingPayoutGateway();
            var first = Task.Run(() => ProcessAsync(cs, gateway));
            await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

            await Assert.ThrowsAsync<DomainException>(() => ProcessAsync(cs, gateway));
            Assert.Equal(1, gateway.CallCount);

            gateway.Release.TrySetResult();
            var result = await first;
            Assert.Equal("Completed", result.Status);
            Assert.Equal("BANK-REF-81001", result.Reference);

            await using var verifyConnection = new SqlConnection(cs);
            await verifyConnection.OpenAsync();
            await using var verify = new SqlCommand("""
                SELECT s.Status, b.AvailableIRR, b.ReservedForSettlementIRR,
                       (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions WHERE SettlementId=81001)
                FROM dbo.Settlements s
                JOIN dbo.SellerBalances b ON b.SellerId=s.SellerId
                WHERE s.Id=81001;
                """, verifyConnection);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((byte)3, reader.GetByte(0));
            Assert.Equal(500_000L, reader.GetInt64(1));
            Assert.Equal(0L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
        }
        finally { await DropAsync(db, master); }
    }

    private static async Task<SettlementResult> ProcessAsync(string cs, ISellerPayoutGateway gateway)
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>().UseSqlServer(cs).Options;
        await using var db = new MarketplaceDbContext(options);
        var service = new SettlementService(
            new LifecycleRepository(db),
            new EfUnitOfWork(db),
            new SqlIdGenerator(db),
            gateway,
            new SellerManagementRepository(db));
        return await service.ProcessAsync(81001);
    }

    private sealed class BlockingPayoutGateway : ISellerPayoutGateway
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(bool Success, string? Reference, string? Error)> TransferAsync(
            string bankName, string iban, string accountHolderName, long amountIRR, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return (true, "BANK-REF-81001", null);
        }
    }

    private static async Task<(string Database, string Target, string Master)> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(configured), "MARKETPLACE_SQLSERVER is required.");
        var db = "MarketplaceSettlementRace_" + Guid.NewGuid().ToString("N");
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        var target = new SqlConnectionStringBuilder(configured) { InitialCatalog = db }.ConnectionString;
        await using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{db}];", connection);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            await using var connection = new SqlConnection(target);
            await connection.OpenAsync();
            await using var command = new SqlCommand(await File.ReadAllTextAsync(path), connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
        catch { await DropAsync(db, master); throw; }
        return (db, target, master);
    }

    private static async Task SeedAsync(string cs, long amount)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            DECLARE @now DATETIME2(7)=SYSUTCDATETIME();
            INSERT dbo.Users(Id,Mobile,PasswordHash,DisplayName,CreatedAtUtc)
            VALUES(71001,N'09120007101',N'test-hash',N'Race Seller',@now);
            INSERT dbo.Sellers(Id,UserId,Status,CommissionRateBasisPoints,MinimumCommissionIRR,MaxStoreCount,CreatedAtUtc,ActivatedAtUtc)
            VALUES(72001,71001,2,1000,0,2,@now,@now);
            INSERT dbo.SellerBankAccounts(Id,SellerId,BankName,Iban,AccountHolderName,IsDefault,IsVerified,CreatedAtUtc)
            VALUES(74001,72001,N'Test Bank',N'IR000000000000000000000000',N'Race Seller',1,1,@now);
            INSERT dbo.SellerBalances(Id,SellerId,AvailableIRR,PendingIRR,BlockedIRR,ReservedForSettlementIRR,LiabilityIRR,UpdatedAtUtc)
            VALUES(80001,72001,@amount,0,0,0,0,@now);
            """, connection);
        command.Parameters.AddWithValue("@amount", amount);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(bool Accepted, long Id)> RequestAsync(string cs, string key, long amount)
    {
        // Exercise the production application service and EF/SQL Server repositories, not a
        // hand-written approximation of the settlement transaction.
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlServer(cs)
            .Options;
        await using var db = new MarketplaceDbContext(options);
        var lifecycle = new LifecycleRepository(db);
        var unitOfWork = new EfUnitOfWork(db);
        var ids = new SqlIdGenerator(db);
        var sellers = new SellerManagementRepository(db);
        var service = new SettlementService(
            lifecycle,
            unitOfWork,
            ids,
            new NotConfiguredSellerPayoutGateway(),
            sellers);

        try
        {
            var result = await service.RequestAsync(71001, 74001, amount, key);
            return (true, result.SettlementId);
        }
        catch (DomainException)
        {
            return (false, 0);
        }
    }

    private static async Task DropAsync(string db, string master)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
