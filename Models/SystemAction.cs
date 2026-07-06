using System.Collections.Generic;

namespace WinButler.Models;

/// <summary>One external command to run (file + arguments), the unit a <see cref="SystemAction"/>
/// executes. These are defined in code, never in the editable definitions JSON — executable
/// commands must not be data-driven.</summary>
public sealed record SystemCommand(string FileName, string Arguments)
{
    public string Display => string.IsNullOrEmpty(Arguments) ? FileName : $"{FileName} {Arguments}";
}

/// <summary>
/// A one-click Windows maintenance action shown on the System Tools page (DISM component cleanup,
/// SFC, Windows Update cache flush, event-log clear, WMI reset). Unlike the scan/clean flow these
/// run external tools rather than deleting files, so they live outside <c>IScanner</c>/<c>ICleaner</c>.
/// </summary>
public sealed class SystemAction
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>The commands this action runs, in order. Also used to preview the action in dry-run.</summary>
    public required IReadOnlyList<SystemCommand> Steps { get; init; }

    /// <summary>Extra caution shown in the confirm modal (empty for low-risk actions).</summary>
    public string Warning { get; init; } = "";

    /// <summary>Read-only actions (e.g. DISM /AnalyzeComponentStore) change nothing, so they run for
    /// real even in dry-run and never prompt for confirmation.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Flags the most dangerous actions (event-log clear, WMI reset) so the UI can group
    /// them under an "Advanced" divider with the strongest warnings.</summary>
    public bool IsAdvanced { get; init; }
}

/// <summary>A privacy/MRU cleanup shown on the System Tools page. Distinct from
/// <see cref="SystemAction"/> because it clears history (recent files + registry) rather than running
/// an external tool — the handler is chosen by <see cref="Id"/>.</summary>
public sealed record PrivacyOp(string Id, string Name, string Description);
