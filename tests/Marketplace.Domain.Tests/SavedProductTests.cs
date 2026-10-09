using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SavedProductTests
{
    [Fact]
    public void Create_StoresCustomerProductAndTimestamp()
    {
        var timestamp = new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc);

        var saved = SavedProduct.Create(11, 22, 33, timestamp);

        Assert.Equal(11, saved.Id);
        Assert.Equal(22, saved.CustomerId);
        Assert.Equal(33, saved.ProductId);
        Assert.Equal(timestamp, saved.CreatedAtUtc);
    }

    [Theory]
    [InlineData(0, 22, 33)]
    [InlineData(11, 0, 33)]
    [InlineData(11, 22, 0)]
    public void Create_RejectsInvalidIdentifiers(long id, long customerId, long productId)
    {
        Assert.Throws<DomainException>(() => SavedProduct.Create(id, customerId, productId));
    }
}
