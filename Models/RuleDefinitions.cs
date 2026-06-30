using System;
using System.Collections.Generic;
using System.Linq;

namespace WinButler.Models;

/// <summary>
/// The full set of path rules WinButler uses, in a JSON-serializable shape. This is the single
/// maintainable source of truth: edit <c>Data/definitions.json</c> to add caches or redirect
/// targets — no code changes required. The same shape is what a future remote source (a JSON
/// file on GitHub) would provide, so bundled and online definitions merge seamlessly.
/// </summary>
public sealed class WinButlerDefinitions
{
    /// <summary>Schema version, for forward-compatibility when the format evolves.</summary>
    public int Version { get; set; } = 1;

    public CacheRuleSet Cache { get; set; } = new();
    public RedirectRuleSet Redirect { get; set; } = new();

    /// <summary>
    /// Combines two definition sets. <paramref name="overlay"/> adds to and overrides
    /// <paramref name="baseDefs"/> (used to layer a remote source on top of the bundled one).
    /// </summary>
    public static WinButlerDefinitions Merge(WinButlerDefinitions baseDefs, WinButlerDefinitions overlay)
    {
        return new WinButlerDefinitions
        {
            Version = Math.Max(baseDefs.Version, overlay.Version),
            Cache = new CacheRuleSet
            {
                AlwaysSafeNames = Union(baseDefs.Cache.AlwaysSafeNames, overlay.Cache.AlwaysSafeNames),
                CautionNames = Union(baseDefs.Cache.CautionNames, overlay.Cache.CautionNames),
                CautionPathFragments = Union(baseDefs.Cache.CautionPathFragments, overlay.Cache.CautionPathFragments),
                SafeContextFragments = Union(baseDefs.Cache.SafeContextFragments, overlay.Cache.SafeContextFragments),
                DenyFragments = Union(baseDefs.Cache.DenyFragments, overlay.Cache.DenyFragments),
            },
            Redirect = new RedirectRuleSet
            {
                DenyNames = Union(baseDefs.Redirect.DenyNames, overlay.Redirect.DenyNames),
                Entries = MergeEntries(baseDefs.Redirect.Entries, overlay.Redirect.Entries),
            },
        };
    }

    private static List<string> Union(List<string> a, List<string> b) =>
        a.Concat(b).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Redirect entries are keyed by TargetName; an overlay entry replaces the bundled one.
    private static List<RedirectEntry> MergeEntries(List<RedirectEntry> a, List<RedirectEntry> b)
    {
        var byKey = new Dictionary<string, RedirectEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in a.Concat(b))
            byKey[e.TargetName] = e;
        return byKey.Values.ToList();
    }
}

/// <summary>Rules driving <c>CacheScanner</c>'s safe/caution/deny classification.</summary>
public sealed class CacheRuleSet
{
    /// <summary>Bare folder names that are always safe (unambiguous transient artifacts).</summary>
    public List<string> AlwaysSafeNames { get; set; } = new();

    /// <summary>Bare folder names that look like caches but hold data → always Caution.</summary>
    public List<string> CautionNames { get; set; } = new();

    /// <summary>Path fragments that force Caution (e.g. package stores named "Cache").</summary>
    public List<string> CautionPathFragments { get; set; } = new();

    /// <summary>Path fragments that make a generic "cache" folder safe (known app/tool context).</summary>
    public List<string> SafeContextFragments { get; set; } = new();

    /// <summary>Path fragments that are never touched even if "cache" is in the name.</summary>
    public List<string> DenyFragments { get; set; } = new();
}

/// <summary>Rules driving <c>RedirectionService</c>'s candidate catalog.</summary>
public sealed class RedirectRuleSet
{
    /// <summary>Folder names that are never redirected (security stores).</summary>
    public List<string> DenyNames { get; set; } = new();

    public List<RedirectEntry> Entries { get; set; } = new();
}

/// <summary>One redirectable directory definition (the JSON-facing form of a catalog entry).</summary>
public sealed class RedirectEntry
{
    /// <summary>Path relative to %USERPROFILE% (e.g. ".gradle" or "AppData\\Local\\JetBrains").</summary>
    public string RelativeToProfile { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Unique folder name created under &lt;drive&gt;:\_redirected\. Must be unique.</summary>
    public string TargetName { get; set; } = "";

    public string Category { get; set; } = "Other";
}
