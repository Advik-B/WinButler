using System;
using System.Linq;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Covers <see cref="ScriptCatalog"/> — the data-driven half of the System Tools catalog. The
/// validation cases matter beyond tidiness: this manifest selects what an elevated process runs, so
/// every rejection path is a security boundary, and the load is all-or-nothing so a bad entry can
/// never leave a destructive action registered without its read-only preview.
/// </summary>
public sealed class ScriptCatalogTests
{
    private const string ValidEntry = """
        { "id": "a", "name": "A", "description": "D", "script": "RemoveGhostDevices.ps1", "isReadOnly": true }
        """;

    private static string Manifest(params string[] entries) =>
        $$"""{ "actions": [ {{string.Join(",", entries)}} ] }""";

    [Fact]
    public void Bundled_manifest_registers_the_ghost_device_actions()
    {
        var actions = ScriptCatalog.LoadBundled().Actions;

        var list = actions.Single(a => a.Id == "ghost-devices-list");
        Assert.True(list.IsReadOnly);
        Assert.False(list.IsAdvanced);

        var remove = actions.Single(a => a.Id == "ghost-devices-remove");
        Assert.True(remove.IsAdvanced);
        Assert.False(remove.IsReadOnly);
        Assert.Contains("no undo", remove.Warning, StringComparison.OrdinalIgnoreCase);

        // Both drive the SAME script, so the read-only preview can't drift from what Remove does.
        Assert.All(actions, a => Assert.Contains("RemoveGhostDevices.ps1", a.Steps.Single().Display));
    }

    [Fact]
    public void Valid_manifest_parses()
    {
        var actions = ScriptCatalog.Parse(Manifest(ValidEntry));

        var action = Assert.Single(actions);
        Assert.Equal("a", action.Id);
        Assert.Single(action.Steps);
    }

    /// <summary>The load is all-or-nothing: one bad entry must take the whole manifest down rather
    /// than register the good ones. Each case pairs the offender with a VALID entry to prove the
    /// valid one is dropped too.</summary>
    [Theory]
    // A mode is interpolated into the script body, so anything but a bare identifier could inject
    // PowerShell into a process that always runs elevated. This is the case that matters most.
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "RemoveGhostDevices.ps1", "mode": "List'; Remove-Item C:\\ -Recurse #", "isReadOnly": true }""")]
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "RemoveGhostDevices.ps1", "mode": "List\nRemove-Item", "isReadOnly": true }""")]
    // Script must resolve to something embedded in the assembly.
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "NotEmbedded.ps1", "isReadOnly": true }""")]
    // ...and must be a plain file name — no path traversal, no arbitrary extension.
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "..\\..\\evil.ps1", "isReadOnly": true }""")]
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "calc.exe", "isReadOnly": true }""")]
    // A destructive action must state its own risk — the confirm modal shows Warning.
    [InlineData("""{ "id": "b", "name": "B", "description": "D", "script": "RemoveGhostDevices.ps1" }""")]
    // Required metadata.
    [InlineData("""{ "id": "", "name": "B", "description": "D", "script": "RemoveGhostDevices.ps1", "isReadOnly": true }""")]
    [InlineData("""{ "id": "b", "name": "", "description": "D", "script": "RemoveGhostDevices.ps1", "isReadOnly": true }""")]
    [InlineData("""{ "id": "b", "name": "B", "description": "", "script": "RemoveGhostDevices.ps1", "isReadOnly": true }""")]
    public void Invalid_entry_rejects_the_whole_manifest(string badEntry)
    {
        Assert.ThrowsAny<Exception>(() => ScriptCatalog.Parse(Manifest(ValidEntry, badEntry)));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        Assert.ThrowsAny<Exception>(() => ScriptCatalog.Parse(Manifest(ValidEntry, ValidEntry)));
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => ScriptCatalog.Parse("{ not valid json "));
    }

    [Fact]
    public void A_read_only_entry_needs_no_warning()
    {
        // isReadOnly actions never reach the confirm modal, so the warning requirement doesn't apply.
        Assert.Single(ScriptCatalog.Parse(Manifest(ValidEntry)));
    }
}
