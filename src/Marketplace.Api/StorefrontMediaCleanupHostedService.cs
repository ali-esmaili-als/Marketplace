using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api;

/// <summary>
/// Removes stale storefront upload files that are no longer referenced by active media records.
/// Only files matching the API's generated GUID filename convention are eligible.
/// </summary>
public sealed class StorefrontMediaCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ILogger<StorefrontMediaCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinimumFileAge = TimeSpan.FromHours(24);
    private const string UrlPrefix = "/uploads/storefront/";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunCleanupSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task RunCleanupSafelyAsync(CancellationToken ct)
    {
        try
        {
            var root = environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(environment.ContentRootPath, "wwwroot");

            var storefrontRoot = Path.GetFullPath(Path.Combine(root, "uploads", "storefront"));
            if (!Directory.Exists(storefrontRoot))
                return;

            HashSet<string> activeUrls;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
                var urls = await db.StorefrontMedia.AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => x.Url)
                    .ToListAsync(ct);
                activeUrls = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
            }

            var cutoffUtc = DateTime.UtcNow - MinimumFileAge;
            var examined = 0;
            var deleted = 0;
            var errors = 0;

            foreach (var storeDirectory in Directory.EnumerateDirectories(storefrontRoot))
            {
                ct.ThrowIfCancellationRequested();

                var directoryInfo = new DirectoryInfo(storeDirectory);
                if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !long.TryParse(directoryInfo.Name, out var storeId) || storeId <= 0)
                    continue;

                foreach (var filePath in Directory.EnumerateFiles(storeDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    examined++;

                    try
                    {
                        var info = new FileInfo(filePath);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                            info.LastWriteTimeUtc >= cutoffUtc ||
                            !IsGeneratedImageName(info.Name))
                            continue;

                        var expectedUrl = $"{UrlPrefix}{storeId}/{info.Name}";
                        if (activeUrls.Contains(expectedUrl))
                            continue;

                        File.Delete(filePath);
                        deleted++;
                    }
                    catch (IOException ex)
                    {
                        errors++;
                        logger.LogWarning(ex, "Could not remove orphaned storefront media file {FilePath}.", filePath);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        errors++;
                        logger.LogWarning(ex, "Access denied while removing orphaned storefront media file {FilePath}.", filePath);
                    }
                }
            }

            if (deleted > 0 || errors > 0)
                logger.LogInformation(
                    "Storefront media cleanup completed. Examined {Examined}, deleted {Deleted}, errors {Errors}.",
                    examined, deleted, errors);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storefront media cleanup cycle failed; a later cycle will retry.");
        }
    }

    private static bool IsGeneratedImageName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length == 32 && stem.All(Uri.IsHexDigit);
    }
}
