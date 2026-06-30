using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Finds "cache"-named folders, scoped strictly to the AppData roots (never the whole
/// drive, Documents, or source trees). All risk classification is delegated to
/// <see cref="SafeCaches"/> so the curated knowledge lives in one place.
/// </summary>
public sealed class CacheScanner : IScanner
{
    public CleanupCategory Category => CleanupCategory.Cache;
    public string Title => "Cache folders";

    // 6 is enough to reach Chromium's deepest cache (…\User Data\Default\Service Worker\CacheStorage).
    private const int MaxDepth = 6;

    private readonly SafeCaches _safeCaches;

    public CacheScanner(SafeCaches safeCaches) => _safeCaches = safeCaches;

    /// <summary>Convenience overload using the bundled definitions (tests/standalone).</summary>
    public CacheScanner() : this(SafeCaches.FromBundled()) { }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<CleanupTarget>>(() => Scan(ct), ct);

    private IReadOnlyList<CleanupTarget> Scan(CancellationToken ct)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // Roaming
        };

        var results = new List<CleanupTarget>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
                Walk(root, depth: 0, results, ct);
        }

        return results;
    }

    private void Walk(string dir, int depth, List<CleanupTarget> results, CancellationToken ct)
    {
        if (depth > MaxDepth)
            return;
        ct.ThrowIfCancellationRequested();

        // Never descend into reparse points (junctions/symlinks).
        try
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                return;
        }
        catch { return; }

        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(dir); }
        catch { return; }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(child);

            if (_safeCaches.IsDenied(child))
                continue;

            if (SafeCaches.IsCacheName(name))
            {
                var size = DirectorySizeCalculator.GetSize(child, ct);
                if (size == 0)
                    continue;

                var risk = _safeCaches.Classify(child);
                results.Add(new CleanupTarget
                {
                    FullPath = child,
                    DisplayName = TrimForDisplay(child),
                    Category = CleanupCategory.Cache,
                    SizeBytes = size,
                    Risk = risk,
                    Reason = SafeCaches.Reason(risk),
                });

                // Don't recurse into a matched cache (avoids nested duplicate targets).
                continue;
            }

            Walk(child, depth + 1, results, ct);
        }
    }

    private static string TrimForDisplay(string path)
    {
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase)
            ? "~" + path.Substring(userRoot.Length)
            : path;
    }
}
