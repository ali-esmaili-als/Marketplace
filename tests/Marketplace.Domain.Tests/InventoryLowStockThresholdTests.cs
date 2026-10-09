using Marketplace.Domain.Common;
using Marketplace.Domain.Inventory;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class InventoryLowStockThresholdTests
{
    [Fact]
    public void Create_UsesDefaultThresholdOfFive()
    {
        var inventory = InventoryItem.Create(1, 10, 5);

        Assert.Equal(5L, inventory.LowStockThreshold);
        Assert.True(inventory.IsLowStock);
    }

    [Fact]
    public void IsLowStock_UsesAvailableStockRatherThanTotalStock()
    {
        var inventory = InventoryItem.Create(1, 10, 20);
        inventory.SetLowStockThreshold(5);
        inventory.Reserve(15);

        Assert.Equal(5L, inventory.AvailableQuantity);
        Assert.True(inventory.IsLowStock);
    }

    [Fact]
    public void IsLowStock_IsFalseWhenOutOfStock()
    {
        var inventory = InventoryItem.Create(1, 10, 5);
        inventory.SetLowStockThreshold(5);
        inventory.Reserve(5);

        Assert.Equal(0L, inventory.AvailableQuantity);
        Assert.False(inventory.IsLowStock);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    public void SetLowStockThreshold_RejectsValuesOutsideDatabaseRange(long threshold)
    {
        var inventory = InventoryItem.Create(1, 10);

        Assert.Throws<DomainException>(() => inventory.SetLowStockThreshold(threshold));
    }

    [Fact]
    public void SetLowStockThreshold_AcceptsZero()
    {
        var inventory = InventoryItem.Create(1, 10, 1);

        inventory.SetLowStockThreshold(0);

        Assert.Equal(0L, inventory.LowStockThreshold);
        Assert.False(inventory.IsLowStock);
    }
}
