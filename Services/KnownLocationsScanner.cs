using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services.Definitions;

namespace WinButler.Services;

/// <summary>
/// Scans the known-locations catalog — a data-driven list of specific app, game, launcher and
/// Windows junk paths (caches, logs, crash dumps) distilled from community cleanup knowledge and
/// stored in <c>Data/definitions/</c>. Every candidate is gated through <see cref="SafeCaches.IsDenied"/>
/// (the deny-list holds here as everywhere), junctions are never followed, and each entry's declared
/// risk drives the hybrid delete policy (Safe → permanent, Caution/Risky → Recycle Bin).
/// </summary>
public sealed class KnownLocationsScanner : IScanner
{
    // Enough to reach crash dumps nested a few levels down (e.g. Firefox profile *.dmp files)
    // without walking arbitrarily deep in "files" recursive mode.
    private const int MaxFileDepth = 6;

    private readonly SafeCaches _safeCaches;
    private readonly IReadOnlyList<KnownLocationEntry> _entries;

    public KnownLocationsScanner(SafeCaches safeCaches, KnownLocationRuleSet rules)
    {
        _safeCaches = safeCaches;
        _entries = rules.Entries;
    }

    /// <summary>Convenience overload using the bundled definitions (tests/standalone).</summary>
    public KnownLocationsScanner() : this(SafeCaches.FromBundled(), BundledDefinitionSource.Load().KnownLocations) { }

    public CleanupCategory Category => CleanupCategory.Apps;
    public string Title => "App & game leftovers";

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<CleanupTarget>>(() => Scan(ct), ct);

    private IReadOnlyList<CleanupTarget> Scan(CancellationToken ct)
    {
        var results = new List<CleanupTarget>();
        foreach (var entry in _entries)
        {
            ct.ThrowIfCancellationRequested();
            ScanEntry(entry, results, ct);
        }
        return results;
    }

    /// <summary>Scans one catalog entry (internal as the test seam — pass an entry whose
    /// <see cref="KnownLocationEntry.Path"/> is an absolute fixture path).</summary>
    internal void ScanEntry(KnownLocationEntry entry, List<CleanupTarget> results, CancellationToken ct)
    {
        var risk = ParseRisk(entry.Risk);

        if (entry.AllDrives)
        {
            var fragment = ExpandTokens(entry.Path);
            foreach (var root in FixedDriveRoots())
                ScanPath(entry, Path.Combine(root, fragment), risk, results, ct);
        }
        else
        {
            ScanPath(entry, ExpandTokens(entry.Path), risk, results, ct);
        }
    }

    private void ScanPath(KnownLocationEntry entry, string fullPath, RiskLevel risk,
        List<CleanupTarget> results, CancellationToken ct)
    {
        switch (entry.Mode?.Trim().ToLowerInvariant())
        {
            case "self":
                AddSingle(entry, fullPath, risk, results, ct);
                break;
            case "files":
                AddFiles(entry, fullPath, risk, results, ct);
                break;
            case "children":
            default:
                AddChildren(entry, fullPath, risk, results, ct);
                break;
        }
    }

    /// <summary>"self" — the path itself (file or whole directory) is one target.</summary>
    private void AddSingle(KnownLocationEntry entry, string path, RiskLevel risk,
        List<CleanupTarget> results, CancellationToken ct)
    {
        var isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path))
            return;
        if (_safeCaches.IsDenied(path))
            return;

        long size = SizeOf(path, isDir, ct);
        if (size == 0)
            return;

        results.Add(Target(entry, path, entry.DisplayName, size, risk));
    }

    /// <summary>"children" — each immediate child (file or folder) of the directory is a target;
    /// the directory itself is left in place (mirrors <see cref="TempScanner"/>).</summary>
    private void AddChildren(KnownLocationEntry entry, string dir, RiskLevel risk,
        List<CleanupTarget> results, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var child in SafeEnumerate(() => Directory.EnumerateFileSystemEntries(dir)))
        {
            ct.ThrowIfCancellationRequested();
            if (_safeCaches.IsDenied(child))
                continue;

            var isDir = Directory.Exists(child);
            long size = SizeOf(child, isDir, ct);
            if (size == 0)
                continue;

            results.Add(Target(entry, child, $"{entry.DisplayName} · {Path.GetFileName(child)}", size, risk));
        }
    }

    /// <summary>"files" — files under the directory matching the entry's pattern (optionally
    /// recursive). Junction directories are never descended into.</summary>
    private void AddFiles(KnownLocationEntry entry, string dir, RiskLevel risk,
        List<CleanupTarget> results, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return;

        var pattern = string.IsNullOrWhiteSpace(entry.Pattern) ? "*" : entry.Pattern;
        foreach (var file in EnumerateFilesSafe(dir, pattern, entry.Recursive, depth: 0, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (_safeCaches.IsDenied(file))
                continue;

            long size;
            try { size = new FileInfo(file).Length; }
            catch { continue; }
            if (size == 0)
                continue;

            results.Add(Target(entry, file, $"{entry.DisplayName} · {Path.GetFileName(file)}", size, risk));
        }
    }

    private CleanupTarget Target(KnownLocationEntry entry, string path, string displayName, long size, RiskLevel risk)
        => new()
        {
            FullPath = path,
            DisplayName = displayName,
            Category = CleanupCategory.Apps,
            SizeBytes = size,
            Risk = risk,
            Reason = string.IsNullOrWhiteSpace(entry.Description) ? entry.DisplayName : entry.Description,
            GroupKey = entry.Group, // future-proofing: lets a grouped view cluster by domain
        };

    private static long SizeOf(string path, bool isDir, CancellationToken ct)
    {
        try
        {
            return isDir ? DirectorySizeCalculator.GetSize(path, ct) : new FileInfo(path).Length;
        }
        catch { return 0; }
    }

    /// <summary>Recursively yields files matching <paramref name="pattern"/>, skipping reparse-point
    /// directories so a junction is never followed into its target.</summary>
    private static IEnumerable<string> EnumerateFilesSafe(string dir, string pattern, bool recursive, int depth, CancellationToken ct)
    {
        // Don't descend through a junction/symlink.
        try
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                yield break;
        }
        catch { yield break; }

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(dir, pattern)))
            yield return file;

        if (!recursive || depth >= MaxFileDepth)
            yield break;

        foreach (var sub in SafeEnumerate(() => Directory.EnumerateDirectories(dir)))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in EnumerateFilesSafe(sub, pattern, recursive: true, depth + 1, ct))
                yield return file;
        }
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate(); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> FixedDriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return Enumerable.Empty<string>(); }

        return drives
            .Where(d =>
            {
                try { return d.DriveType == DriveType.Fixed && d.IsReady; }
                catch { return false; }
            })
            .Select(d => d.RootDirectory.FullName);
    }

    private static RiskLevel ParseRisk(string risk) => risk?.Trim().ToLowerInvariant() switch
    {
        "safe" => RiskLevel.Safe,
        "risky" => RiskLevel.Risky,
        _ => RiskLevel.Caution, // most-careful default for anything unrecognised
    };

    /// <summary>Expands the catalog's path tokens: the custom <c>%Documents%</c>/<c>%LocalLow%</c>
    /// first, then the standard environment variables (<c>%AppData%</c>, <c>%WinDir%</c>, …).</summary>
    internal static string ExpandTokens(string path)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localLow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

        var s = path
            .Replace("%Documents%", docs, StringComparison.OrdinalIgnoreCase)
            .Replace("%LocalLow%", localLow, StringComparison.OrdinalIgnoreCase);

        return Environment.ExpandEnvironmentVariables(s);
    }
}
