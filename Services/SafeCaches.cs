using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinButler.Models;
using WinButler.Services.Definitions;

namespace WinButler.Services;

/// <summary>
/// Classifies cache folders using a <see cref="CacheRuleSet"/> (loaded from definitions.json, the
/// maintainable source of truth). <see cref="CacheScanner"/> delegates every decision here.
///
/// Design principle: <b>most-careful wins</b>. A folder is only rated <see cref="RiskLevel.Safe"/>
/// (and pre-selected for deletion) when positively recognised as a transient, regenerable cache.
/// Anything merely suspected stays <see cref="RiskLevel.Caution"/> so the user must opt in.
/// </summary>
public sealed class SafeCaches
{
    private readonly HashSet<string> _alwaysSafeNames;
    private readonly HashSet<string> _cautionNames;
    private readonly string[] _cautionPathFragments;
    private readonly string[] _safeContextFragments;
    private readonly string[] _denyFragments;

    public SafeCaches(CacheRuleSet rules)
    {
        _alwaysSafeNames = new HashSet<string>(rules.AlwaysSafeNames, StringComparer.OrdinalIgnoreCase);
        _cautionNames = new HashSet<string>(rules.CautionNames, StringComparer.OrdinalIgnoreCase);
        _cautionPathFragments = rules.CautionPathFragments.ToArray();
        _safeContextFragments = rules.SafeContextFragments.ToArray();
        _denyFragments = rules.DenyFragments.ToArray();
    }

    /// <summary>Builds an instance from the bundled definitions (convenience for tests/standalone).</summary>
    public static SafeCaches FromBundled() => new(BundledDefinitionSource.Load().Cache);

    /// <summary>True if a folder name looks like a cache at all (cheap pre-filter).</summary>
    public static bool IsCacheName(string name) =>
        name.Contains("cache", StringComparison.OrdinalIgnoreCase);

    /// <summary>Folders we never touch even if the name contains "cache".</summary>
    public bool IsDenied(string fullPath)
    {
        var p = Normalize(fullPath);
        return _denyFragments.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Classifies a cache-named folder. Precedence: caution name/path → always-safe name →
    /// anchored-safe context → caution fallback.
    /// </summary>
    public RiskLevel Classify(string fullPath)
    {
        var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        var path = Normalize(fullPath);

        if (_cautionNames.Contains(name))
            return RiskLevel.Caution;
        if (_cautionPathFragments.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return RiskLevel.Caution;

        if (_alwaysSafeNames.Contains(name))
            return RiskLevel.Safe;

        if (_safeContextFragments.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return RiskLevel.Safe;

        return RiskLevel.Caution;
    }

    /// <summary>Short human explanation used in the UI.</summary>
    public static string Reason(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => "Known regenerable cache",
        RiskLevel.Caution => "Cache-like folder — review before deleting (may hold app data)",
        _ => "Cache folder",
    };

    private static string Normalize(string p) => p.Replace('/', '\\');
}
