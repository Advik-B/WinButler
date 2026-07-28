using System;
using System.Collections.Generic;
using System.Linq;

namespace WinButler.Models;

/// <summary>
/// The full set of path rules WinButler uses, in a JSON-serializable shape. This is the single
/// maintainable source of truth: edit the per-domain files under <c>Data/definitions/</c> to add
/// caches, redirect targets, or known-location cleanups — no code changes required. Each file is a
/// partial of this shape and they are folded together at load; the same shape is what a remote
/// source (a JSON file on GitHub) would provide, so bundled and online definitions merge seamlessly.
/// </summary>
public sealed class WinButlerDefinitions
{
    /// <summary>Schema version, for forward-compatibility when the format evolves.</summary>
    public int Version { get; set; } = 1;

    public CacheRuleSet Cache { get; set; } = new();
    public RedirectRuleSet Redirect { get; set; } = new();
    public KnownLocationRuleSet KnownLocations { get; set; } = new();

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
            KnownLocations = new KnownLocationRuleSet
            {
                Entries = MergeKnownLocations(baseDefs.KnownLocations.Entries, overlay.KnownLocations.Entries),
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

    // Known-location entries are keyed by Id; an overlay entry replaces the bundled one.
    private static List<KnownLocationEntry> MergeKnownLocations(List<KnownLocationEntry> a, List<KnownLocationEntry> b)
    {
        var byKey = new Dictionary<string, KnownLocationEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in a.Concat(b))
            byKey[e.Id] = e;
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

/// <summary>Rules driving <c>KnownLocationsScanner</c> — a catalog of specific app/system junk
/// locations distilled from community cleanup knowledge (caches, logs, crash dumps).</summary>
public sealed class KnownLocationRuleSet
{
    public List<KnownLocationEntry> Entries { get; set; } = new();
}

/// <summary>One known cleanup location (the JSON-facing form of a catalog entry). See
/// <c>Data/definitions/README.md</c> for the field reference.</summary>
public sealed class KnownLocationEntry
{
    /// <summary>Unique id — the merge key. Keep unique across every definitions file.</summary>
    public string Id { get; set; } = "";

    /// <summary>Path with env tokens (e.g. "%LocalAppData%\\Discord\\Cache"). When
    /// <see cref="AllDrives"/> is set this is a fragment appended to every fixed-drive root.</summary>
    public string Path { get; set; } = "";

    /// <summary><c>children</c> (each immediate child is a target, parent kept) | <c>files</c>
    /// (files matching <see cref="Pattern"/>) | <c>self</c> (the path itself is one target).</summary>
    public string Mode { get; set; } = "children";

    /// <summary>Wildcard filter for <c>files</c> mode (e.g. "*.dmp"). Ignored otherwise.</summary>
    public string? Pattern { get; set; }

    /// <summary><c>files</c> mode only: recurse into subdirectories (junctions are not followed).</summary>
    public bool Recursive { get; set; }

    /// <summary><c>children</c> mode only: child names (case-insensitive, not full paths) to skip
    /// even though they'd otherwise match — an additional per-rule carve-out alongside the
    /// deny-list, for a folder that mixes junk with data that must never be offered.</summary>
    public List<string>? Exclude { get; set; }

    /// <summary>When true, <see cref="Path"/> is resolved against every fixed drive root.</summary>
    public bool AllDrives { get; set; }

    /// <summary><c>safe</c> | <c>caution</c> | <c>risky</c> — drives the delete policy.</summary>
    public string Risk { get; set; } = "caution";

    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>UI grouping label (e.g. "Browsers", "Games", "Windows").</summary>
    public string Group { get; set; } = "Other";
}
