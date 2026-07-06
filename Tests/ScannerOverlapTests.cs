using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinButler.Services;
using WinButler.Services.Definitions;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// The known-locations catalog is deliberately curated to complement <see cref="CacheScanner"/> —
/// it targets logs, dumps and non-cache junk that CacheScanner does NOT already sweep. If an entry
/// ever overlaps a CacheScanner target (same path, or one nested inside the other) the dashboard
/// would double-count its size and CLEAN ALL could try to delete it twice.
///
/// This is a REAL-I/O test: it runs both scanners against the machine's actual profile so the check
/// reflects what is truly installed here, not synthetic fixtures. It also doubles as a smoke test
/// that the whole catalog resolves and scans without throwing.
/// </summary>
public sealed class ScannerOverlapTests
{
    [Fact]
    public async Task Catalog_targets_never_overlap_cache_scanner_targets()
    {
        var defs = BundledDefinitionSource.Load();
        var safe = new SafeCaches(defs.Cache);

        var cachePaths = (await new CacheScanner(safe).ScanAsync())
            .Select(t => t.FullPath).ToList();
        var appPaths = (await new KnownLocationsScanner(safe, defs.KnownLocations).ScanAsync())
            .Select(t => t.FullPath).ToList();

        var overlaps = new List<string>();
        foreach (var app in appPaths)
            foreach (var cache in cachePaths)
                if (Overlaps(app, cache))
                    overlaps.Add($"{app}  <=>  {cache}");

        Assert.True(overlaps.Count == 0,
            "Known-locations catalog overlaps CacheScanner (double-count risk):\n" + string.Join("\n", overlaps));
    }

    // True when the two paths are the same, or one is an ancestor of the other (so their sizes overlap).
    private static bool Overlaps(string a, string b)
    {
        a = a.TrimEnd('\\');
        b = b.TrimEnd('\\');
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
