using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Groups known dev-tool roots (JetBrains, Android SDK, .gradle, npm-cache, ...) for the Dev
/// Junk screen. This is deliberately a thin aggregation over two existing pieces, not a new
/// scanner:
///
/// <list type="bullet">
/// <item>Discovery + on-disk size + redirect-target matching comes from
/// <see cref="IRedirectionService.ScanCandidatesAsync"/> (already the catalog of dev-tool
/// roots — <c>Data/definitions.json</c>'s <c>redirect.entries</c>), filtered to the
/// categories that read as "dev junk" rather than general/apps/games. <see cref="BuildAsync"/>
/// takes the candidate list as a parameter rather than calling ScanCandidatesAsync itself —
/// that call is already expensive (it sizes every dev-tool root, including things like the
/// Android SDK) and the Redirect screen already pays that cost once; the caller is expected
/// to pass in the same already-scanned <see cref="RedirectCandidate"/> list rather than
/// triggering a second full scan.</item>
/// <item>The reclaimable subset within each root is found with <see cref="SafeCaches"/> —
/// the exact same rule engine <see cref="CacheScanner"/> uses — via a small walk rooted at
/// the dev-tool folder instead of AppData. This is intentionally a separate walk rather than
/// a call into <see cref="CacheScanner"/>: that scanner hardcodes AppData roots and only
/// recurses into folders literally named "*cache*", which misses dev-tool reclaimable
/// subfolders like <c>.gradle\caches</c> or <c>.cargo\registry\cache</c> that don't carry
/// "cache" in their own folder name (they're still caught here because
/// <see cref="SafeCaches.Classify"/> matches by path fragment, not folder name).</item>
/// </list>
/// </summary>
public sealed class DevJunkAggregator
{
    // Categories from definitions.json's redirect.entries that read as "developer junk".
    // ML caches/Apps/Games are real redirect candidates too, just not "dev junk" specifically.
    private static readonly HashSet<string> InScopeCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Build tools", "Toolchains", "Node.js", "Python", "IDEs", "Web tooling", "Misc dev",
    };

    // Bounded shallow: SafeCaches matches (".gradle\caches", "JetBrains\...\caches", etc.)
    // are near the root in practice, and unlike CacheScanner this walk has no cheap
    // name-based pre-filter to prune early — on a huge, flat SDK tree (Android SDK's
    // build-tools/platforms with no recognised safe fragments) a deeper bound would visit
    // every directory for no payoff.
    private const int MaxDepth = 3;

    private readonly SafeCaches _safeCaches;

    public DevJunkAggregator(SafeCaches safeCaches)
    {
        _safeCaches = safeCaches;
    }

    /// <summary>Convenience overload using the bundled definitions (tests/standalone).</summary>
    public DevJunkAggregator() : this(SafeCaches.FromBundled()) { }

    /// <summary>Builds the Dev Junk groups from an already-scanned candidate list — see the
    /// class remarks on why this doesn't call <see cref="IRedirectionService.ScanCandidatesAsync"/>
    /// itself. Runs the (I/O-bound) reclaimable walk on a background thread.</summary>
    public Task<IReadOnlyList<DevToolGroup>> BuildAsync(
        IReadOnlyList<RedirectCandidate> candidates, CancellationToken ct = default)
        => Task.Run(() => Build(candidates, ct), ct);

    private IReadOnlyList<DevToolGroup> Build(IReadOnlyList<RedirectCandidate> candidates, CancellationToken ct)
    {
        var inScope = candidates.Where(c => InScopeCategories.Contains(c.Category));

        var groups = new List<DevToolGroup>();
        foreach (var c in inScope)
        {
            ct.ThrowIfCancellationRequested();

            // Already-redirected roots are junctions; their "contents" live on the other
            // drive, so there's nothing local left to classify.
            var targets = c.IsAlreadyRedirected
                ? (IReadOnlyList<CleanupTarget>)Array.Empty<CleanupTarget>()
                : FindReclaimable(c.SourcePath, ct);

            groups.Add(new DevToolGroup
            {
                SourcePath = c.SourcePath,
                DisplayName = c.DisplayName,
                Description = c.Description,
                Category = c.Category,
                TargetName = c.TargetName,
                OnDiskBytes = c.SizeBytes,
                ReclaimableBytes = targets.Sum(t => t.SizeBytes),
                ReclaimableTargets = targets,
                IsLocked = Directory.Exists(Path.Combine(c.SourcePath, ".git")),
                IsAlreadyRedirected = c.IsAlreadyRedirected,
            });
        }

        return groups.OrderByDescending(g => g.OnDiskBytes).ToList();
    }

    private List<CleanupTarget> FindReclaimable(string root, CancellationToken ct)
    {
        var results = new List<CleanupTarget>();
        Walk(root, depth: 0, results, ct);
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

            if (_safeCaches.IsDenied(child))
                continue;

            var risk = _safeCaches.Classify(child);
            if (risk == RiskLevel.Safe)
            {
                var size = DirectorySizeCalculator.GetSize(child, ct);
                if (size == 0)
                    continue;

                results.Add(new CleanupTarget
                {
                    FullPath = child,
                    DisplayName = TrimForDisplay(child),
                    Category = CleanupCategory.DevJunk,
                    SizeBytes = size,
                    Risk = risk,
                    Reason = SafeCaches.Reason(risk),
                });

                // Don't recurse into a matched folder (avoids nested duplicate targets).
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
