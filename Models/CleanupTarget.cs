namespace WinButler.Models;

/// <summary>
/// A single file-system location a scanner has flagged for cleanup, together with
/// everything the UI and the cleaner need to act on it.
/// </summary>
public sealed class CleanupTarget
{
    public required string FullPath { get; init; }

    /// <summary>Short label for the UI (e.g. "GitHubDesktop · app-3.5.12").</summary>
    public required string DisplayName { get; init; }

    public required CleanupCategory Category { get; init; }

    /// <summary>Total size on disk in bytes (computed off the UI thread).</summary>
    public long SizeBytes { get; init; }

    public required RiskLevel Risk { get; init; }

    /// <summary>Human-readable explanation of why this was flagged.</summary>
    public required string Reason { get; init; }

    /// <summary>The deletion policy implied by <see cref="Risk"/>.</summary>
    public DeleteMode DeleteMode =>
        Risk == RiskLevel.Safe ? DeleteMode.Permanent : DeleteMode.RecycleBin;

    /// <summary>Optional grouping key for screens that cluster targets by parent app
    /// (currently just <see cref="ElectronLeftoverScanner"/> — one group per Squirrel install).</summary>
    public string? GroupKey { get; init; }

    /// <summary>Optional "kept" version label for the group header, e.g. "v3.5.12 · KEPT".</summary>
    public string? CurrentVersionLabel { get; init; }
}
