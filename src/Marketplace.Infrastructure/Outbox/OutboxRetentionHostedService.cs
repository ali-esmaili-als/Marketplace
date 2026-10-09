using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Outbox;

/// <summary>
/// Archives only successfully processed Outbox rows after a configured retention period.
/// Archiving is disabled by default and never touches financial/ledger tables.
/// </summary>
public sealed class OutboxRetentionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxRetentionHostedService> _logger;

    public OutboxRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("Outbox:Retention:Enabled"))
        {
            _logger.LogInformation("Outbox retention is disabled. No Outbox rows will be archived or deleted.");
            return;
        }

        var intervalMinutes = Math.Clamp(_configuration.GetValue("Outbox:Retention:IntervalMinutes", 60), 5, 1440);
        var retentionDays = Math.Clamp(_configuration.GetValue("Outbox:Retention:ProcessedRetentionDays", 90), 7, 3650);
        var batchSize = Math.Clamp(_configuration.GetValue("Outbox:Retention:BatchSize", 500), 1, 5000);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
                var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

                var archived = await db.Database.ExecuteSqlInterpolatedAsync($@"
;WITH candidates AS
(
    SELECT TOP ({batchSize}) *
    FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = N'Processed'
      AND ProcessedAtUtc IS NOT NULL
      AND ProcessedAtUtc < {cutoffUtc}
    ORDER BY ProcessedAtUtc, Id
)
DELETE FROM candidates
OUTPUT
    deleted.Id, deleted.MessageId, deleted.EventType, deleted.PayloadJson,
    deleted.OccurredAtUtc, deleted.ProcessedAtUtc, deleted.LockedUntilUtc,
    deleted.LockToken, deleted.NextAttemptAtUtc, deleted.Attempts,
    deleted.Status, deleted.LastError
INTO dbo.OutboxMessageArchive
    (Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc,
     LockedUntilUtc, LockToken, NextAttemptAtUtc, Attempts, Status, LastError);",
                    stoppingToken);

                if (archived > 0)
                    _logger.LogInformation("Archived {ArchivedCount} processed Outbox messages older than {CutoffUtc}.",
                        archived, cutoffUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox retention iteration failed. Source rows remain protected by the SQL transaction.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
