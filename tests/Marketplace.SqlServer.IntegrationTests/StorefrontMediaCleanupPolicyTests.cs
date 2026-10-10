using Marketplace.Api;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>Regression tests for the deletion safeguards used by the background cleanup service.</summary>
public sealed class StorefrontMediaCleanupPolicyTests
{
    private static readonly DateTime Cutoff = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string FileName = "8a2e7f0d4b6c4f3e9a1b2c3d4e5f6071.webp";
    private const string Url = "/uploads/storefront/73011/8a2e7f0d4b6c4f3e9a1b2c3d4e5f6071.webp";

    [Fact]
    public void Deletes_old_unreferenced_generated_image()
    {
        Assert.True(StorefrontMediaCleanupPolicy.ShouldDelete(
            FileName, Cutoff.AddDays(-1), Cutoff, FileAttributes.Normal, Url,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Keeps_file_still_referenced_by_active_media()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Url };
        Assert.False(StorefrontMediaCleanupPolicy.ShouldDelete(
            FileName, Cutoff.AddDays(-1), Cutoff, FileAttributes.Normal, Url, active));
    }

    [Fact]
    public void Keeps_file_when_active_url_differs_only_by_case()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Url.ToUpperInvariant() };
        Assert.False(StorefrontMediaCleanupPolicy.ShouldDelete(
            FileName, Cutoff.AddDays(-1), Cutoff, FileAttributes.Normal, Url, active));
    }

    [Fact]
    public void Keeps_recent_file_even_when_unreferenced()
    {
        Assert.False(StorefrontMediaCleanupPolicy.ShouldDelete(
            FileName, Cutoff, Cutoff, FileAttributes.Normal, Url,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Keeps_reparse_point_even_when_old_and_unreferenced()
    {
        Assert.False(StorefrontMediaCleanupPolicy.ShouldDelete(
            FileName, Cutoff.AddDays(-2), Cutoff, FileAttributes.ReparsePoint, Url,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("product-photo.webp")]
    [InlineData("8a2e7f0d4b6c4f3e9a1b2c3d4e5f607.webp")]
    [InlineData("8a2e7f0d4b6c4f3e9a1b2c3d4e5f607g.webp")]
    [InlineData("8a2e7f0d4b6c4f3e9a1b2c3d4e5f6071.gif")]
    [InlineData("8a2e7f0d4b6c4f3e9a1b2c3d4e5f6071.txt")]
    public void Keeps_files_outside_generated_image_convention(string fileName)
    {
        Assert.False(StorefrontMediaCleanupPolicy.ShouldDelete(
            fileName, Cutoff.AddDays(-2), Cutoff, FileAttributes.Normal,
            $"/uploads/storefront/73011/{fileName}", new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }
}
