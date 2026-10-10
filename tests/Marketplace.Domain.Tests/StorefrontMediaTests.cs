using Marketplace.Domain.Catalog;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class StorefrontMediaTests
{
    [Fact]
    public void Create_AllowsStoreLogoWithoutProduct()
    {
        var media = StorefrontMedia.Create(1, 2, null, "logo", "/uploads/storefront/2/logo.webp", "image/webp", "Store logo", 0);

        Assert.Equal("logo", media.Kind);
        Assert.Null(media.ProductId);
        Assert.True(media.IsActive);
    }

    [Fact]
    public void Create_RequiresProductIdForProductImage()
    {
        Assert.Throws<ArgumentException>(() =>
            StorefrontMedia.Create(1, 2, null, "product", "/uploads/storefront/2/item.png", "image/png", null, 0));
    }

    [Fact]
    public void Create_RejectsUnsupportedImageTypes()
    {
        Assert.Throws<ArgumentException>(() =>
            StorefrontMedia.Create(1, 2, null, "logo", "/uploads/storefront/2/logo.svg", "image/svg+xml", null, 0));
    }

    [Fact]
    public void Deactivate_HidesMediaWithoutChangingItsUrl()
    {
        var media = StorefrontMedia.Create(1, 2, null, "banner", "/uploads/storefront/2/banner.jpg", "image/jpeg", null, 0);

        media.Deactivate();

        Assert.False(media.IsActive);
        Assert.Equal("/uploads/storefront/2/banner.jpg", media.Url);
    }
}
