using System.Collections.Generic;

namespace WinButler.Models;

/// <summary>The parsed <c>Scripts/scripts.json</c> manifest — the script-backed half of the System
/// Tools catalog. See <c>Scripts/README.md</c> for the field reference.</summary>
public sealed class ScriptActionManifest
{
    public List<ScriptActionEntry> Actions { get; set; } = new();
}

/// <summary>
/// One script-backed <see cref="SystemAction"/> declared in <c>Scripts/scripts.json</c>. This is
/// metadata plus a <em>reference</em> to an embedded script — deliberately never a command line, an
/// executable name, or raw PowerShell. <see cref="Script"/> must resolve to a <c>.ps1</c> embedded
/// from <c>Scripts/</c> and <see cref="Mode"/> must be a bare identifier, so the set of things this
/// manifest can execute is fixed at compile time. See <see cref="SystemCommand"/>'s note on why
/// executable commands are never data-driven.
/// </summary>
public sealed class ScriptActionEntry
{
    /// <summary>Unique id within the manifest; also the key tests and logs refer to.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Extra caution shown in the confirm modal. Required unless <see cref="IsReadOnly"/>.</summary>
    public string Warning { get; set; } = "";

    /// <summary>File name of an embedded <c>Scripts/*.ps1</c> (e.g. "RemoveGhostDevices.ps1").</summary>
    public string Script { get; set; } = "";

    /// <summary>Optional bare identifier passed to the script as <c>$Mode</c>, letting one script
    /// back several actions (e.g. a read-only "List" preview and the real "Remove").</summary>
    public string? Mode { get; set; }

    /// <summary>Read-only actions change nothing, so they run for real even in dry-run and never
    /// prompt for confirmation.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Groups the action under the UI's "Advanced" divider with the strongest warnings.</summary>
    public bool IsAdvanced { get; set; }
}
