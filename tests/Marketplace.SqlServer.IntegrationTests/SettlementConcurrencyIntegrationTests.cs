using Microsoft.Data.SqlClient;
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
            var a = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "same", 600_000, 91001); });
            var b = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "same", 600_000, 91002); });
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
            var a = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "key-a", 700_000, 92001); });
            var b = Task.Run(async () => { await start.Task; return await RequestAsync(cs, "key-b", 700_000, 92002); });
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

    private static async Task<(bool Accepted, long Id)> RequestAsync(string cs, string key, long amount, long id)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await using (var existing = new SqlCommand("SELECT Id,AmountIRR FROM dbo.Settlements WITH (UPDLOCK,HOLDLOCK) WHERE SellerId=72001 AND RequestKey=@key;", connection, tx))
            {
                existing.Parameters.AddWithValue("@key", key);
                await using var reader = await existing.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var oldId = reader.GetInt64(0);
                    var oldAmount = reader.GetInt64(1);
                    await reader.CloseAsync();
                    await tx.CommitAsync();
                    return (oldAmount == amount, oldId);
                }
            }
            long available;
            long reserved;
            await using (var balance = new SqlCommand("SELECT AvailableIRR,ReservedForSettlementIRR FROM dbo.SellerBalances WITH (UPDLOCK,HOLDLOCK) WHERE SellerId=72001;", connection, tx))
            await using (var reader = await balance.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                available = reader.GetInt64(0);
                reserved = reader.GetInt64(1);
            }
            if (amount <= 0 || amount > available - reserved) { await tx.RollbackAsync(); return (false, 0); }

            var now = DateTime.UtcNow;
            await using var write = new SqlCommand("""
                INSERT dbo.Settlements(Id,SellerId,RequestKey,AmountIRR,Status,BankAccountId,BankNameSnapshot,IbanSnapshot,AccountHolderNameSnapshot,RequestedAtUtc)
                VALUES(@id,72001,@key,@amount,1,74001,N'Test Bank',N'IR000000000000000000000000',N'Race Seller',@now);
                UPDATE dbo.SellerBalances SET ReservedForSettlementIRR=ReservedForSettlementIRR+@amount,
                    UpdatedAtUtc=@now WHERE SellerId=72001;
                INSERT dbo.BalanceTransactions(Id,SellerId,SettlementId,Type,Bucket,AmountIRR,BalanceBeforeIRR,BalanceAfterIRR,Reference,CreatedAtUtc)
                VALUES(@ledger,72001,@id,4,4,@amount,@reserved,@reserved+@amount,N'SETTLEMENT_REQUESTED',@now);
                """, connection, tx);
            write.Parameters.AddWithValue("@id", id);
            write.Parameters.AddWithValue("@ledger", id + 100000);
            write.Parameters.AddWithValue("@key", key);
            write.Parameters.AddWithValue("@amount", amount);
            write.Parameters.AddWithValue("@reserved", reserved);
            write.Parameters.AddWithValue("@now", now);
            await write.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return (true, id);
        }
        catch { try { await tx.RollbackAsync(); } catch { } throw; }
    }

    private static async Task DropAsync(string db, string master)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
