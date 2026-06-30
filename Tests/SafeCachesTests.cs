using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public class SafeCachesTests
{
    private readonly SafeCaches _sc = SafeCaches.FromBundled();

    [Theory]
    // Unambiguous transient artifacts → Safe wherever they are.
    [InlineData(@"C:\X\User Data\Default\GPUCache", RiskLevel.Safe)]
    [InlineData(@"C:\X\User Data\Default\Code Cache", RiskLevel.Safe)]
    // Generic "Cache" is Safe only inside a recognised context.
    [InlineData(@"C:\Users\Me\AppData\Local\Brave\User Data\Default\Cache", RiskLevel.Safe)]
    [InlineData(@"C:\Users\Me\AppData\Local\NuGet\v3-cache", RiskLevel.Safe)]
    [InlineData(@"C:\Users\Me\AppData\Local\JetBrains\Rider2026.1\caches", RiskLevel.Safe)]
    // Data-bearing or unknown → Caution.
    [InlineData(@"C:\Users\Me\AppData\Local\Pub\Cache", RiskLevel.Caution)]
    [InlineData(@"C:\X\User Data\Default\Service Worker\CacheStorage", RiskLevel.Caution)]
    [InlineData(@"C:\X\User Data\Default\blob_storage", RiskLevel.Caution)]
    [InlineData(@"C:\Users\Me\AppData\Local\TotallyUnknownApp\cache", RiskLevel.Caution)]
    public void Classify_matches_expected(string path, RiskLevel expected)
    {
        Assert.Equal(expected, _sc.Classify(path));
    }

    [Theory]
    [InlineData(@"C:\Users\Me\.ssh\cache")]
    [InlineData(@"C:\Users\Me\.gnupg\cache")]
    [InlineData(@"C:\X\User Data\Default\IndexedDB\cache")]
    [InlineData(@"C:\X\User Data\Default\Login Data\cache")]
    public void Denied_paths_are_never_touched(string path)
    {
        Assert.True(_sc.IsDenied(path));
    }

    [Theory]
    [InlineData("Cache", true)]
    [InlineData("GPUCache", true)]
    [InlineData("CacheStorage", true)]
    [InlineData("logs", false)]
    [InlineData("node_modules", false)]
    public void IsCacheName_detects_cache_folders(string name, bool expected)
    {
        Assert.Equal(expected, SafeCaches.IsCacheName(name));
    }
}
