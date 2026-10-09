using Marketplace.Application.Abstractions;
using Marketplace.Domain.Finance;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Outbox;

/// <summary>
/// Durable at-least-once dispatcher. It is disabled by default until a real IOutboxPublisher
/// transport is registered and configured.
/// </summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;

    public OutboxDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("Outbox:Enabled"))
        {
            _logger.LogInformation("Transactional outbox dispatcher is disabled. Set Outbox:Enabled only after registering a real IOutboxPublisher.");
            return;
        }

        var pollMs = Math.Clamp(_configuration.GetValue("Outbox:PollIntervalMilliseconds", 2000), 250, 60000);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetService<IOutboxPublisher>();
                if (publisher is null)
                {
                    _logger.LogError("Outbox is enabled but no IOutboxPublisher is registered. No messages were claimed.");
                    await Task.Delay(TimeSpan.FromMilliseconds(pollMs), stoppingToken);
                    continue;
                }

                var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
                var batchSize = Math.Clamp(_configuration.GetValue("Outbox:BatchSize", 25), 1, 200);
                var leaseSeconds = Math.Clamp(_configuration.GetValue("Outbox:LeaseSeconds", 60), 10, 3600);
                var maxAttempts = Math.Clamp(_configuration.GetValue("Outbox:MaxAttempts", 8), 1, 50);
                var now = DateTime.UtcNow;
                var token = Guid.NewGuid();
                var leaseUntil = now.AddSeconds(leaseSeconds);

                // The update claims rows atomically. READPAST lets multiple instances work
                // concurrently without waiting on rows already locked by another worker.
                await db.Database.ExecuteSqlInterpolatedAsync($@"
;WITH candidates AS
(
    SELECT TOP ({batchSize}) *
    FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE NextAttemptAtUtc <= {now}
      AND (Status = N'Pending' OR (Status = N'Processing' AND LockedUntilUtc <= {now}))
    ORDER BY Id
)
UPDATE candidates
SET Status = N'Processing',
    Attempts = Attempts + 1,
    LockedUntilUtc = {leaseUntil},
    LockToken = {token},
    LastError = NULL;", stoppingToken);

                var claimed = await db.OutboxMessages
                    .Where(x => x.LockToken == token && x.Status == "Processing")
                    .OrderBy(x => x.Id)
                    .ToListAsync(stoppingToken);

                foreach (var message in claimed)
                {
                    try
                    {
                        await publisher.PublishAsync(
                            new OutboxEnvelope(message.MessageId, message.EventType, message.PayloadJson, message.OccurredAtUtc),
                            stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception publishError)
                    {
                        var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(message.Attempts, 10)));
                        message.MarkFailed(DateTime.UtcNow, publishError.Message, maxAttempts, TimeSpan.FromSeconds(delaySeconds));
                        try
                        {
                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogWarning(publishError, "Outbox publish failed for {MessageId}; attempt {Attempt}/{MaxAttempts}, status {Status}.",
                                message.MessageId, message.Attempts, maxAttempts, message.Status);
                        }
                        catch (DbUpdateConcurrencyException concurrency)
                        {
                            _logger.LogWarning(concurrency, "Outbox lease was lost while recording failure for {MessageId}.", message.MessageId);
                            db.Entry(message).State = EntityState.Detached;
                        }
                        catch (Exception persistenceError)
                        {
                            // Leave the row Processing. Its lease will expire and another worker
                            // will retry it; never overwrite a possibly newer owner.
                            _logger.LogError(persistenceError, "Could not persist outbox failure for {MessageId}; lease expiry will recover it.", message.MessageId);
                            db.Entry(message).State = EntityState.Detached;
                        }
                        continue;
                    }

                    try
                    {
                        message.MarkProcessed(DateTime.UtcNow);
                        await db.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Published outbox message {MessageId} ({EventType}).", message.MessageId, message.EventType);
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        // The lease expired and another worker reclaimed this message. Do not
                        // overwrite the newer worker's state; downstream must deduplicate.
                        _logger.LogWarning(ex, "Outbox lease was lost while finalizing message {MessageId}.", message.MessageId);
                        db.Entry(message).State = EntityState.Detached;
                    }
                    catch (Exception ex)
                    {
                        // Publishing succeeded but finalization did not. Delivery may happen
                        // again after lease expiry, which is why consumers deduplicate MessageId.
                        _logger.LogError(ex, "Published outbox message {MessageId}, but could not persist completion; it may be delivered again.", message.MessageId);
                        db.Entry(message).State = EntityState.Detached;
                    }
                }

                if (claimed.Count == 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(pollMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher iteration failed.");
                try { await Task.Delay(TimeSpan.FromMilliseconds(pollMs), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
