using Marketplace.Application.Abstractions;
using Marketplace.Domain.Notifications;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Notifications;

public sealed class LowStockSmsService(
    MarketplaceDbContext db,
    IEnumerable<ISmsProvider> providers,
    IIdGenerator ids,
    ILogger<LowStockSmsService> logger)
{
    public async Task NotifyIfLowAsync(long variantId, CancellationToken ct = default)
    {
        var details = await (from inventory in db.InventoryItems
            join variant in db.ProductVariants on inventory.ProductVariantId equals variant.Id
            join product in db.Products on variant.ProductId equals product.Id
            join store in db.Stores on product.StoreId equals store.Id
            join seller in db.Sellers on store.SellerId equals seller.Id
            join user in db.Users on seller.UserId equals user.Id
            where variant.Id == variantId
            select new
            {
                Inventory = inventory, UserId = user.Id, user.Mobile,
                StoreName = store.Name, ProductName = product.Name, VariantKey = variant.VariantKey
            }).SingleOrDefaultAsync(ct);

        if (details is null || !details.Inventory.IsLowStock || details.Inventory.LowStockAlertSent)
        {
            if (details is not null) details.Inventory.ResetLowStockAlert();
            return;
        }

        var automation = await db.SmsAutomationSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (automation is null || !automation.AutomaticSmsEnabled || !automation.LowStockSmsEnabled) return;

        var selected = await db.SmsProviderSettings.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder).FirstOrDefaultAsync(ct);
        if (selected is null)
        {
            logger.LogWarning("Low-stock SMS was not sent for variant {VariantId}: no SMS provider is enabled.", variantId);
            return;
        }
        var provider = providers.FirstOrDefault(x => string.Equals(x.Name, selected.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            logger.LogWarning("Low-stock SMS provider {Provider} is not registered.", selected.Provider);
            return;
        }

        var message = $"هشدار کم‌موجودی فروشگاه «{details.StoreName}»: محصول «{details.ProductName}»، تنوع «{details.VariantKey}»؛ موجودی قابل فروش {details.Inventory.AvailableQuantity} عدد است (حد هشدار: {details.Inventory.LowStockThreshold}).";
        var notification = Notification.Create(await ids.NextAsync(ct), details.UserId, NotificationChannel.Sms,
            "هشدار کم‌موجودی", message, "LowStock", variantId);
        try
        {
            await provider.SendMessageAsync(details.Mobile, message, ct);
            notification.MarkSent();
            details.Inventory.MarkLowStockAlertSent();
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.Fail();
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Low-stock SMS failed for variant {VariantId}.", variantId);
        }
    }
}
