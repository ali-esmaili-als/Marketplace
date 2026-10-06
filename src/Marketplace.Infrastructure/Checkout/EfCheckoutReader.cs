using Marketplace.Application.Checkout.Ports;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Checkout;

public sealed class EfCheckoutReader(MarketplaceDbContext db) : ICheckoutReader
{
    public async Task<CheckoutCartSnapshot?> GetCartAsync(long cartId, long customerId, CancellationToken cancellationToken = default)
    {
        var cart = await db.Carts.AsNoTracking()
            .Where(x => x.Id == cartId && x.CustomerId == customerId)
            .Select(x => new { x.Id, x.CustomerId, x.StoreId, x.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (cart is null) return null;

        var rows = await db.CartItems.AsNoTracking()
            .Where(x => x.CartId == cartId)
            .Select(x => new
            {
                x.ProductId, x.ProductVariantId, x.Quantity,
                UnitPrice = x.CachedUnitPriceIRR,
                x.WarrantyId, x.WarrantyPriceIRR
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new CheckoutCartSnapshot(cart.Id, cart.CustomerId, cart.StoreId, []);

        var productIds = rows.Select(x => x.ProductId).Distinct().ToArray();
        var variantIds = rows.Select(x => x.ProductVariantId).Distinct().ToArray();

        var products = await db.Products.AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.StoreId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var variants = await db.ProductVariants.AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ProductId, x.Sku, x.VariantKey, x.PriceIRR, x.IsActive })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var variantDiscounts = await db.VariantDiscounts.AsNoTracking()
            .Where(x => variantIds.Contains(x.VariantId) && x.IsActive && x.StartAtUtc <= now && now < x.EndAtUtc)
            .OrderByDescending(x => x.StartAtUtc)
            .Select(x => new { x.VariantId, x.DiscountType, x.DiscountValue })
            .ToListAsync(cancellationToken);

        var productDiscounts = await db.ProductDiscounts.AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId) && x.IsActive && x.StartAtUtc <= now && now < x.EndAtUtc)
            .OrderByDescending(x => x.StartAtUtc)
            .Select(x => new { x.ProductId, x.DiscountType, x.DiscountValue })
            .ToListAsync(cancellationToken);

        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(x => x.StoreId == cart.StoreId && x.IsActive && x.StartAtUtc <= now && now < x.EndAtUtc)
            .Select(x => new { x.Id, x.Name, x.StartAtUtc })
            .ToListAsync(cancellationToken);
        var campaignIds = campaigns.Select(x => x.Id).ToArray();

        var campaignVariants = campaignIds.Length == 0 ? [] : await db.CampaignVariants.AsNoTracking()
            .Where(x => campaignIds.Contains(x.CampaignId) && variantIds.Contains(x.VariantId))
            .Select(x => new { x.CampaignId, x.ProductId, x.VariantId, x.DiscountType, x.DiscountValue })
            .ToListAsync(cancellationToken);

        var campaignProducts = campaignIds.Length == 0 ? [] : await db.CampaignProducts.AsNoTracking()
            .Where(x => campaignIds.Contains(x.CampaignId) && productIds.Contains(x.ProductId))
            .Select(x => new { x.CampaignId, x.ProductId, x.DiscountType, x.DiscountValue })
            .ToListAsync(cancellationToken);

        var warrantyIds = rows.Where(x => x.WarrantyId.HasValue).Select(x => x.WarrantyId!.Value).Distinct().ToArray();
        var warranties = warrantyIds.Length == 0 ? new Dictionary<long, (string Name, long Price)>() :
            await db.Warranties.AsNoTracking()
                .Where(x => warrantyIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => (Name: x.Name, Price: x.PriceIRR), cancellationToken);

        var items = new List<CheckoutCartItemSnapshot>(rows.Count);
        foreach (var row in rows)
        {
            if (!products.TryGetValue(row.ProductId, out var product) ||
                !variants.TryGetValue(row.ProductVariantId, out var variant) ||
                !variant.IsActive)
                throw new InvalidOperationException($"Cart item {row.ProductVariantId} is no longer available.");

            var variantDiscount = variantDiscounts
                .Where(x => x.VariantId == row.ProductVariantId)
                .Select(x => new DiscountSnapshot(x.DiscountType, x.DiscountValue))
                .FirstOrDefault();
            var productDiscount = productDiscounts
                .Where(x => x.ProductId == row.ProductId)
                .Select(x => new DiscountSnapshot(x.DiscountType, x.DiscountValue))
                .FirstOrDefault();

            CampaignDiscountSnapshot? campaignDiscount = null;
            var cv = campaignVariants.Where(x => x.VariantId == row.ProductVariantId)
                .OrderByDescending(x => campaigns.First(c => c.Id == x.CampaignId).StartAtUtc)
                .FirstOrDefault();
            if (cv is not null)
            {
                var c = campaigns.First(x => x.Id == cv.CampaignId);
                campaignDiscount = new(c.Id, c.Name, cv.DiscountType, cv.DiscountValue);
            }
            else
            {
                var cp = campaignProducts.Where(x => x.ProductId == row.ProductId)
                    .OrderByDescending(x => campaigns.First(c => c.Id == x.CampaignId).StartAtUtc)
                    .FirstOrDefault();
                if (cp is not null)
                {
                    var c = campaigns.First(x => x.Id == cp.CampaignId);
                    campaignDiscount = new(c.Id, c.Name, cp.DiscountType, cp.DiscountValue);
                }
            }

            string? warrantyName = null;
            var warrantyPrice = row.WarrantyPriceIRR;
            if (row.WarrantyId is long wid && warranties.TryGetValue(wid, out var warranty))
            {
                warrantyName = warranty.Name;
                warrantyPrice = warranty.Price;
            }

            items.Add(new CheckoutCartItemSnapshot(
                row.ProductId, row.ProductVariantId, row.Quantity,
                variant.PriceIRR ?? row.UnitPrice, product.Name, variant.VariantKey,
                variant.Sku, row.WarrantyId, warrantyName, warrantyPrice,
                variantDiscount, productDiscount, campaignDiscount));
        }

        return new CheckoutCartSnapshot(cart.Id, cart.CustomerId, cart.StoreId, items);
    }
}