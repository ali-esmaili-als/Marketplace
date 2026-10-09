using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Orders;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Persistence;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Real SQL Server regression coverage for concurrent refund submissions.
/// The gateway is deliberately blocked and then times out, simulating an ambiguous bank outcome.
/// </summary>
[Collection("FinancialIntegrityHttpTests")]
public sealed class RefundConcurrencyIntegrationTests
{
    [Fact]
    public async Task Concurrent_refund_requests_for_same_order_call_gateway_once_and_keep_ambiguous_result_processing()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER");
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString),
            "MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");

        const string databaseName = "MarketplaceRefundConcurrencyIntegrationTests";
        var masterBuilder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
        var targetBuilder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = databaseName };
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

        try
        {
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script was not copied to test output: {bootstrapPath}");
            await using (var connection = new SqlConnection(targetBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, await File.ReadAllTextAsync(bootstrapPath));
                await ExecuteAsync(connection, """
                    DECLARE @now DATETIME2(7)=SYSUTCDATETIME();
                    INSERT dbo.Users(Id,Mobile,PasswordHash,DisplayName,CreatedAtUtc)
                    VALUES (71011,N'09120007111',N'test-hash-customer',N'Refund Customer',@now),
                           (71012,N'09120007112',N'test-hash-seller',N'Refund Seller',@now);
                    INSERT dbo.Sellers(Id,UserId,Status,CommissionRateBasisPoints,MinimumCommissionIRR,MaxStoreCount,CreatedAtUtc,ActivatedAtUtc)
                    VALUES (72011,71012,2,1000,0,1,@now,@now);
                    INSERT dbo.Stores(Id,SellerId,Name,Slug,Status,CommissionRateBasisPoints,MinimumCommissionIRR,CreatedAtUtc)
                    VALUES (73011,72011,N'Refund Store',N'refund-store',2,1000,0,@now);
                    INSERT dbo.Orders
                        (Id,CustomerId,SellerId,StoreId,SubtotalAmountIRR,ShippingFeeIRR,CampaignDiscountIRR,CouponDiscountIRR,
                         TotalAmountIRR,SellerAmountIRR,Status,CreatedAtUtc,PaidAtUtc,DeliveryExpiresAtUtc)
                    VALUES (75011,71011,72011,73011,100000,0,0,0,100000,90000,6,@now,@now,DATEADD(day,-1,@now));
                    INSERT dbo.Payments
                        (Id,OrderId,CustomerId,AmountIRR,Status,Provider,Authority,ReferenceNumber,CreatedAtUtc,PaidAtUtc)
                    VALUES (76011,75011,71011,100000,3,N'TestBank',N'AUTH-REFUND-11',N'BANK-PAYMENT-11',@now,@now);
                    """);
            }

            var gateway = new BlockingTimeoutRefundGateway();
            var firstTask = ProcessRefundAsync(targetBuilder.ConnectionString, gateway);
            await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // The first call has committed its Processing reservation before reaching the gateway.
            // A second request must see it and must not issue another external refund.
            await Assert.ThrowsAsync<DomainException>(() =>
                ProcessRefundAsync(targetBuilder.ConnectionString, gateway));
            Assert.Equal(1, gateway.CallCount);

            gateway.Release.TrySetResult();
            await Assert.ThrowsAsync<TimeoutException>(() => firstTask);
            Assert.Equal(1, gateway.CallCount);

            await using var verifyConnection = new SqlConnection(targetBuilder.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var verify = new SqlCommand("""
                SELECT COUNT_BIG(*),
                       MIN(Status),
                       MAX(Status),
                       (SELECT COUNT_BIG(*) FROM dbo.BalanceTransactions WHERE RefundId IN (SELECT Id FROM dbo.Refunds WHERE OrderId=75011)),
                       (SELECT Status FROM dbo.Orders WHERE Id=75011),
                       (SELECT Status FROM dbo.Payments WHERE OrderId=75011)
                FROM dbo.Refunds
                WHERE OrderId=75011;
                """, verifyConnection);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal((byte)3, reader.GetByte(1)); // Processing: bank result is uncertain.
            Assert.Equal((byte)3, reader.GetByte(2));
            Assert.Equal(0L, reader.GetInt64(3)); // No financial effects before a confirmed refund.
            Assert.Equal((byte)7, reader.GetByte(4)); // RefundRequested.
            Assert.Equal((byte)3, reader.GetByte(5)); // Payment remains Succeeded.
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

    private static async Task ProcessRefundAsync(string connectionString, IPaymentGateway gateway)
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new MarketplaceDbContext(options);
        var service = new RefundService(
            new OrderRepository(db),
            new PaymentRepository(db),
            new LifecycleRepository(db),
            new EfUnitOfWork(db),
            new SqlIdGenerator(db),
            new TestPaymentGatewayFactory(gateway));
        await service.ProcessAsync(75011, RefundReason.DeliveryExpired);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestPaymentGatewayFactory(IPaymentGateway gateway) : IPaymentGatewayFactory
    {
        public Task<IPaymentGateway> GetForExistingPaymentAsync(PaymentProviderCode provider, CancellationToken ct = default)
            => Task.FromResult(gateway);
        public Task<IPaymentGateway> GetAsync(PaymentProviderCode provider, CancellationToken ct = default)
            => Task.FromResult(gateway);
        public Task<IReadOnlyList<PaymentProviderInfo>> GetAvailableAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PaymentProviderInfo>>(Array.Empty<PaymentProviderInfo>());
    }

    private sealed class BlockingTimeoutRefundGateway : IPaymentGateway
    {
        private int _callCount;
        public string ProviderName => "TestBank";
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PaymentRedirect> CreatePaymentAsync(long paymentId, long orderId, long amountIRR, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<PaymentVerification> VerifyAsync(string authority, long amountIRR, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public async Task<bool> RefundAsync(string? paymentReference, long amountIRR, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw new TimeoutException("Simulated ambiguous bank refund timeout.");
        }
    }
}
