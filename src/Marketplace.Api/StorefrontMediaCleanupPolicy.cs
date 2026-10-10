namespace Marketplace.Api;

/// <summary>Pure safety policy for deleting unreferenced storefront upload files.</summary>
public static class StorefrontMediaCleanupPolicy
{
    public static bool ShouldDelete(
        string fileName,
        DateTime lastWriteTimeUtc,
        DateTime cutoffUtc,
        FileAttributes attributes,
        string expectedUrl,
        ISet<string> activeUrls)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(expectedUrl);
        ArgumentNullException.ThrowIfNull(activeUrls);

        if ((attributes & FileAttributes.ReparsePoint) != 0 || lastWriteTimeUtc >= cutoffUtc)
            return false;

        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length != 32 || !stem.All(Uri.IsHexDigit))
            return false;

        return !activeUrls.Contains(expectedUrl);
    }
}
