using Marketplace.Domain.Common;
using Marketplace.Domain.Inventory;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class InventoryItemThresholdTests
{
    [Fact]
    public void New_inventory_uses_default_low_stock_threshold()
    {
        var item = InventoryItem.Create(1, 2, 20);

        Assert.Equal(5, item.LowStockThreshold);
        Assert.False(item.IsLowStock);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Low_stock_state_uses_available_quantity_and_configured_threshold(long available, bool expected)
    {
        var item = InventoryItem.Create(1, 2, available);
        item.SetLowStockThreshold(5);

        Assert.Equal(expected, item.IsLowStock);
    }

    [Fact]
    public void Reserved_stock_counts_toward_low_stock_state()
    {
        var item = InventoryItem.Create(1, 2, 10);
        item.Reserve(7);
        item.SetLowStockThreshold(3);

        Assert.Equal(3, item.AvailableQuantity);
        Assert.True(item.IsLowStock);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000000001)]
    public void Threshold_outside_supported_range_is_rejected(long threshold)
    {
        var item = InventoryItem.Create(1, 2, 10);

        Assert.Throws<DomainException>(() => item.SetLowStockThreshold(threshold));
    }

    [Fact]
    public void Zero_threshold_disables_low_stock_warning_for_positive_stock()
    {
        var item = InventoryItem.Create(1, 2, 1);
        item.SetLowStockThreshold(0);

        Assert.False(item.IsLowStock);
    }
}
