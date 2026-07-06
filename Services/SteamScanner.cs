using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services.Definitions;
using WinButler.Services.Steam;

namespace WinButler.Services;

/// <summary>
/// Cleans Steam's throwaway data. Steam's install is discovered from the registry and its library
/// folders from <c>libraryfolders.vdf</c>, then each library's shader/download caches, dumps and logs
/// are flagged, plus the user-level Steam web caches. No process is killed — a locked file (Steam
/// running) simply surfaces as a skipped target, matching the app's behaviour elsewhere. Every path is
/// gated through <see cref="SafeCaches.IsDenied"/> and junctions are never followed.
/// </summary>
public sealed class SteamScanner : IScanner
{
    private readonly SafeCaches _safeCaches;
    private readonly SteamLocator _locator;

    public SteamScanner(SafeCaches safeCaches, SteamLocator locator)
    {
        _safeCaches = safeCaches;
        _locator = locator;
    }

    /// <summary>Convenience overload using the bundled definitions and the real registry.</summary>
    public SteamScanner() : this(SafeCaches.FromBundled(), new SteamLocator()) { }

    public CleanupCategory Category => CleanupCategory.Steam;
    public string Title => "Steam junk";

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<CleanupTarget>>(() => Scan(ct), ct);

    private IReadOnlyList<CleanupTarget> Scan(CancellationToken ct)
    {
        var results = new List<CleanupTarget>();

        var steamPath = _locator.FindSteamPath();
        if (steamPath is not null)
        {
            foreach (var library in DiscoverLibraries(steamPath))
            {
                ct.ThrowIfCancellationRequested();
                ScanLibrary(library, results, ct);
            }
        }

        ScanUserCaches(results, ct);
        return results;
    }

    /// <summary>The Steam install plus every library folder in libraryfolders.vdf (the install lists
    /// itself as library 0). Falls back to just the install dir if the VDF is missing/unreadable.</summary>
    internal IReadOnlyList<string> DiscoverLibraries(string steamPath)
    {
        var libraries = new List<string> { steamPath };

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
                libraries.AddRange(VdfLibraryParser.ParseLibraryPaths(File.ReadAllText(vdf)));
        }
        catch { /* unreadable VDF — the install dir alone is still worth cleaning */ }

        return libraries
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ScanLibrary(string library, List<CleanupTarget> results, CancellationToken ct)
    {
        var label = Path.GetFileName(library.TrimEnd('\\')) is { Length: > 0 } n ? n : library;

        // Shader/download/temp caches — regenerable. Workshop downloads are Caution: deleting them
        // restarts any in-progress workshop subscription download.
        AddChildren(Path.Combine(library, "config", "overlayhtmlcache"), RiskLevel.Safe, $"{label} · overlay cache", results, ct);
        AddChildren(Path.Combine(library, "dumps"), RiskLevel.Safe, $"{label} · dumps", results, ct);
        AddChildren(Path.Combine(library, "logs"), RiskLevel.Safe, $"{label} · logs", results, ct);
        AddChildren(Path.Combine(library, "steamapps", "temp"), RiskLevel.Safe, $"{label} · temp", results, ct);
        AddChildren(Path.Combine(library, "steamapps", "shadercache"), RiskLevel.Safe, $"{label} · shader cache", results, ct);
        AddChildren(Path.Combine(library, "steamapps", "workshop", "temp"), RiskLevel.Safe, $"{label} · workshop temp", results, ct);
        AddChildren(Path.Combine(library, "steamapps", "workshop", "downloads"), RiskLevel.Caution, $"{label} · workshop downloads", results, ct);

        // Stray crash dumps at the library root.
        AddFiles(library, "*.mdmp", RiskLevel.Safe, $"{label} · crash dump", results, ct);
    }

    private void ScanUserCaches(List<CleanupTarget> results, CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AddChildren(Path.Combine(local, "Steam", "htmlcache"), RiskLevel.Safe, "Steam · htmlcache", results, ct);
        AddChildren(Path.Combine(local, "Steam", "html5app", "htmlcache"), RiskLevel.Safe, "Steam · html5 cache", results, ct);
        AddChildren(Path.Combine(local, "CEF", "User Data", "Crashpad", "reports"), RiskLevel.Safe, "Steam · CEF crash reports", results, ct);
    }

    private void AddChildren(string dir, RiskLevel risk, string label, List<CleanupTarget> results, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var child in SafeEnumerate(() => Directory.EnumerateFileSystemEntries(dir)))
        {
            ct.ThrowIfCancellationRequested();
            if (_safeCaches.IsDenied(child))
                continue;

            var isDir = Directory.Exists(child);
            long size;
            try { size = isDir ? DirectorySizeCalculator.GetSize(child, ct) : new FileInfo(child).Length; }
            catch { continue; }
            if (size == 0)
                continue;

            results.Add(Target(child, $"{label} · {Path.GetFileName(child)}", size, risk));
        }
    }

    private void AddFiles(string dir, string pattern, RiskLevel risk, string label, List<CleanupTarget> results, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(dir, pattern)))
        {
            ct.ThrowIfCancellationRequested();
            if (_safeCaches.IsDenied(file))
                continue;

            long size;
            try { size = new FileInfo(file).Length; }
            catch { continue; }
            if (size == 0)
                continue;

            results.Add(Target(file, $"{label} · {Path.GetFileName(file)}", size, risk));
        }
    }

    private static CleanupTarget Target(string path, string displayName, long size, RiskLevel risk) => new()
    {
        FullPath = path,
        DisplayName = displayName,
        Category = CleanupCategory.Steam,
        SizeBytes = size,
        Risk = risk,
        Reason = risk == RiskLevel.Caution
            ? "In-progress workshop download — deleting restarts it"
            : "Steam regenerable cache/log/dump",
    };

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate(); }
        catch { return Enumerable.Empty<string>(); }
    }
}
