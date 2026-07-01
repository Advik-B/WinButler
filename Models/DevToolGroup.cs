using System.Collections.Generic;

namespace WinButler.Models;

/// <summary>
/// One dev-tool root (JetBrains, Android SDK, .gradle, ...) with its on-disk footprint,
/// the safely-reclaimable portion within it, and whether it can be redirected instead.
/// Built by <see cref="WinButler.Services.DevJunkAggregator"/> from existing
/// <see cref="RedirectCandidate"/> discovery + <see cref="WinButler.Services.SafeCaches"/>
/// classification — no new scanning logic, just a new aggregation layer.
/// </summary>
public sealed class DevToolGroup
{
    public required string SourcePath { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }

    /// <summary>Matches the corresponding <see cref="RedirectCandidate.TargetName"/> so the
    /// "Redirect →" action can hand off to the existing redirect flow for the same folder.</summary>
    public required string TargetName { get; init; }

    /// <summary>Total size on disk.</summary>
    public long OnDiskBytes { get; init; }

    /// <summary>Sum of the Safe-classified reclaimable subpaths within this root.</summary>
    public long ReclaimableBytes { get; init; }

    /// <summary>The actual reclaimable subpaths, so cleaning can reuse <see cref="ICleaner"/>
    /// unchanged instead of duplicating delete logic.</summary>
    public IReadOnlyList<CleanupTarget> ReclaimableTargets { get; init; } = System.Array.Empty<CleanupTarget>();

    /// <summary>True if a <c>.git</c> folder sits directly under this root — the concrete,
    /// checkable signal for "this is a tracked, protected location" (e.g. dotfiles cloned
    /// under version control), matching the mockup's ".dotfiles — protected" case.</summary>
    public bool IsLocked { get; init; }

    public bool IsAlreadyRedirected { get; init; }
}
