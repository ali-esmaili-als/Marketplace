using System.Security.Claims;
using Marketplace.Api.Auth;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Catalog;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api;

public static class StoreMediaEndpoints
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };

    public static IEndpointRouteBuilder MapStoreMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/stores/{storeId:long}/media", async (long storeId, MarketplaceDbContext db, CancellationToken ct) =>
        {
            var items = await db.StorefrontMedia.AsNoTracking()
                .Where(x => x.StoreId == storeId && x.IsActive)
                .OrderBy(x => x.Kind).ThenBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc)
                .Select(x => new { x.Id, x.StoreId, x.ProductId, x.Kind, x.Url, x.ContentType, x.AltText, x.SortOrder })
                .ToListAsync(ct);
            return Results.Ok(items);
        }).AllowAnonymous();

        app.MapPost("/api/sellers/me/stores/{storeId:long}/media", async (
            ClaimsPrincipal user, long storeId, IFormFile file, string kind, long? productId, string? altText,
            int? sortOrder, MarketplaceDbContext db, Marketplace.Application.Abstractions.ISellerManagementRepository sellers,
            IIdGenerator ids, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var seller = await sellers.GetSellerByUserIdAsync(CurrentUserId(user), ct);
            if (seller is null) return Results.Unauthorized();
            var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId, ct);
            if (store is null) return Results.NotFound();
            if (store.SellerId != seller.Id) return Results.Forbid();
            return await SaveAsync(storeId, file, kind, productId, altText, sortOrder, db, ids, env, ct);
        }).RequirePermission("Seller.Catalog.Manage").DisableAntiforgery();

        app.MapPost("/api/admin/stores/{storeId:long}/media", async (
            long storeId, IFormFile file, string kind, long? productId, string? altText,
            int? sortOrder, MarketplaceDbContext db, IIdGenerator ids, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId, ct);
            if (store is null) return Results.NotFound();
            return await SaveAsync(storeId, file, kind, productId, altText, sortOrder, db, ids, env, ct);
        }).RequirePermission("Admin.Identity.Manage").DisableAntiforgery();

        app.MapDelete("/api/sellers/me/stores/{storeId:long}/media/{mediaId:long}", async (
            ClaimsPrincipal user, long storeId, long mediaId, MarketplaceDbContext db,
            Marketplace.Application.Abstractions.ISellerManagementRepository sellers, CancellationToken ct) =>
        {
            var seller = await sellers.GetSellerByUserIdAsync(CurrentUserId(user), ct);
            if (seller is null) return Results.Unauthorized();
            var media = await db.StorefrontMedia.SingleOrDefaultAsync(x => x.Id == mediaId && x.StoreId == storeId, ct);
            if (media is null) return Results.NotFound();
            var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId, ct);
            if (store is null || store.SellerId != seller.Id) return Results.Forbid();
            media.Deactivate();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission("Seller.Catalog.Manage");

        app.MapDelete("/api/admin/stores/{storeId:long}/media/{mediaId:long}", async (
            long storeId, long mediaId, MarketplaceDbContext db, CancellationToken ct) =>
        {
            var media = await db.StorefrontMedia.SingleOrDefaultAsync(x => x.Id == mediaId && x.StoreId == storeId, ct);
            if (media is null) return Results.NotFound();
            media.Deactivate();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission("Admin.Identity.Manage");

        return app;
    }

    private static async Task<IResult> SaveAsync(
        long storeId, IFormFile file, string kind, long? productId, string? altText, int? sortOrder,
        MarketplaceDbContext db, IIdGenerator ids, IWebHostEnvironment env, CancellationToken ct)
    {
        kind = kind?.Trim().ToLowerInvariant() ?? "";
        if (kind is not ("logo" or "banner" or "product")) return Results.BadRequest(new { error = "kind must be logo, banner or product." });
        if ((kind == "product") != productId.HasValue) return Results.BadRequest(new { error = "productId is required only for product media." });
        if (file is null || file.Length is <= 0 or > MaxImageBytes) return Results.BadRequest(new { error = "Image size must be between 1 byte and 5 MB." });
        if (!AllowedTypes.Contains(file.ContentType)) return Results.BadRequest(new { error = "Only JPEG, PNG and WebP images are supported." });
        if (kind == "product" && !await db.Products.AsNoTracking().AnyAsync(x => x.Id == productId && x.StoreId == storeId, ct))
            return Results.BadRequest(new { error = "Product does not belong to this store." });

        var ext = file.ContentType.ToLowerInvariant() switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".webp" };
        var root = env.WebRootPath;
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(env.ContentRootPath, "wwwroot");
        var folder = Path.Combine(root, "uploads", "storefront", storeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(folder, fileName);
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var header = new byte[12];
                var read = await input.ReadAsync(header.AsMemory(0, header.Length), ct);
                if (!HasValidSignature(header.AsSpan(0, read), file.ContentType))
                {
                    output.Close();
                    File.Delete(fullPath);
                    return Results.BadRequest(new { error = "File content does not match its image type." });
                }
                await output.WriteAsync(header.AsMemory(0, read), ct);
                await input.CopyToAsync(output, ct);
            }

            var media = StorefrontMedia.Create(await ids.NextAsync(ct), storeId, productId, kind,
                $"/uploads/storefront/{storeId}/{fileName}", file.ContentType.ToLowerInvariant(), altText, Math.Max(0, sortOrder ?? 0));
            db.StorefrontMedia.Add(media);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/public/stores/{storeId}/media", new
            {
                media.Id, media.StoreId, media.ProductId, media.Kind, media.Url, media.ContentType,
                media.AltText, media.SortOrder, media.CreatedAtUtc
            });
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> bytes, string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
        "image/webp" => bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
        _ => false
    };

    private static long CurrentUserId(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id : throw new UnauthorizedAccessException();
}
