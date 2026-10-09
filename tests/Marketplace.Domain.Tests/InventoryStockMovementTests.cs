using Marketplace.Domain.Common;
using Marketplace.Domain.Inventory;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class InventoryStockMovementTests
{
    [Fact]
    public void Movement_captures_before_after_delta_and_reason()
    {
        var movement = InventoryStockMovement.Create(1, 2, 3, 10, 15, "New delivery");

        Assert.Equal(10, movement.PreviousStockQuantity);
        Assert.Equal(15, movement.NewStockQuantity);
        Assert.Equal(5, movement.QuantityDelta);
        Assert.Equal("New delivery", movement.Reason);
        Assert.NotEqual(default, movement.CreatedAtUtc);
    }

    [Theory]
    [InlineData(0, 2, 3, 1, 2, "valid")]
    [InlineData(1, 0, 3, 1, 2, "valid")]
    [InlineData(1, 2, 0, 1, 2, "valid")]
    [InlineData(1, 2, 3, -1, 2, "valid")]
    [InlineData(1, 2, 3, 1, -2, "valid")]
    [InlineData(1, 2, 3, 1, 2, " ")]
    public void Invalid_movement_is_rejected(long id, long variantId, long sellerId,
        long previous, long next, string reason)
    {
        Assert.Throws<DomainException>(() =>
            InventoryStockMovement.Create(id, variantId, sellerId, previous, next, reason));
    }

    [Fact]
    public void Reason_over_500_characters_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            InventoryStockMovement.Create(1, 2, 3, 5, 6, new string('x', 501)));
    }
}
