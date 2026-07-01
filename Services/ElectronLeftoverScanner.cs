using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Finds stale versions left behind by Squirrel/electron auto-updaters.
///
/// A Squirrel install is a parent folder containing <c>Update.exe</c> and one or more
/// <c>app-X.Y.Z</c> version folders. On update the new <c>app-*</c> is added but the old
/// one is frequently left on disk. We keep only the highest version and flag the rest.
///
/// We deliberately key on the <c>app-</c> prefix + sibling <c>Update.exe</c> signature and
/// NOT on bare version-numbered folders: Chrome/Edge/component-updater dirs (PowerShell,
/// WidevineCdm, FileTypePolicies, "Crowd Deny", …) are also version-named, and deleting
/// those breaks the host app.
/// </summary>
public sealed class ElectronLeftoverScanner : IScanner
{
    public CleanupCategory Category => CleanupCategory.ElectronLeftover;
    public string Title => "Old Electron app versions";

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<CleanupTarget>>(() => Scan(ct), ct);

    private static IReadOnlyList<CleanupTarget> Scan(CancellationToken ct)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            localAppData,
            Path.Combine(localAppData, "Programs"),
        };

        var results = new List<CleanupTarget>();
        var seenParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            // A Squirrel parent is a direct child of a root (e.g. ...\GitHubDesktop).
            foreach (var parent in SafeEnumerateDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                if (!seenParents.Add(parent))
                    continue;

                ScanParent(parent, results, ct);
            }
        }

        return results;
    }

    private static void ScanParent(string parent, List<CleanupTarget> results, CancellationToken ct)
    {
        // Squirrel signature: Update.exe lives alongside the app-* folders.
        if (!File.Exists(Path.Combine(parent, "Update.exe")))
            return;

        var versions = new List<(string Path, Version Version, string Raw)>();
        foreach (var dir in SafeEnumerateDirectories(parent))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseVersion(name.Substring(4), out var version))
                versions.Add((dir, version, name));
        }

        // Need at least two versions for one to be "left behind".
        if (versions.Count < 2)
            return;

        var newest = versions.Max(v => v.Version)!;
        var newestRaw = versions.First(v => v.Version == newest).Raw;
        var appName = Path.GetFileName(parent);
        var currentVersionLabel = $"{newestRaw} · KEPT";

        foreach (var (path, version, raw) in versions)
        {
            ct.ThrowIfCancellationRequested();
            if (version == newest)
                continue; // keep the current version

            var size = DirectorySizeCalculator.GetSize(path, ct);
            results.Add(new CleanupTarget
            {
                FullPath = path,
                DisplayName = $"{appName} · {raw}",
                Category = CleanupCategory.ElectronLeftover,
                SizeBytes = size,
                Risk = RiskLevel.Safe, // superseded program files; the live version stays
                Reason = $"Old version; keeping the current app-{newest}",
                GroupKey = appName,
                CurrentVersionLabel = currentVersionLabel,
            });
        }
    }

    /// <summary>
    /// Parses the numeric portion of a Squirrel version (e.g. "3.5.12", "1.0.0-beta2").
    /// Uses real numeric comparison so app-3.5.11 &gt; app-3.5.2 (string sort gets this wrong).
    /// </summary>
    private static bool TryParseVersion(string raw, out Version version)
    {
        version = new Version(0, 0);

        // Trim any pre-release/build suffix: keep leading digits and dots.
        int end = 0;
        while (end < raw.Length && (char.IsDigit(raw[end]) || raw[end] == '.'))
            end++;
        var numeric = raw.Substring(0, end).TrimEnd('.');
        if (numeric.Length == 0)
            return false;

        // Version requires at least major.minor.
        if (!numeric.Contains('.'))
            numeric += ".0";

        return Version.TryParse(numeric, out version!);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Enumerable.Empty<string>(); }
    }
}
