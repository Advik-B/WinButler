using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services.Definitions;

/// <summary>
/// Central access point for path-rule definitions. Holds an ordered list of sources and exposes a
/// merged, cached <see cref="Current"/> result. Starts with the bundled source loaded synchronously;
/// call <see cref="AddSource"/> + <see cref="RefreshAsync"/> to layer remote definitions on top.
/// </summary>
public sealed class DefinitionsProvider
{
    private readonly List<IDefinitionSource> _sources = new();

    public WinButlerDefinitions Current { get; private set; }

    /// <summary>
    /// True if the bundled definitions couldn't be loaded. <see cref="Current"/> is then an EMPTY
    /// ruleset, and the caller MUST fail closed — an empty ruleset means an empty deny-list, so
    /// scanning with it would offer everything for deletion. The shell responds by constructing no
    /// scanners and showing a persistent error, never by scanning against the empty rules.
    /// </summary>
    public bool LoadFailed { get; }

    public DefinitionsProvider() : this(BundledDefinitionSource.TryLoad) { }

    /// <summary>Test seam: supply the bundled loader (return null to simulate a load failure).</summary>
    internal DefinitionsProvider(Func<WinButlerDefinitions?> bundledLoader)
    {
        // Bundled definitions are always available and load synchronously.
        _sources.Add(new BundledDefinitionSource());
        var loaded = bundledLoader();
        if (loaded is null)
        {
            LoadFailed = true;
            Current = new WinButlerDefinitions(); // empty — the shell fails closed on this
        }
        else
        {
            Current = loaded;
        }
    }

    /// <summary>Registers an additional source (e.g. a <see cref="RemoteDefinitionSource"/>).</summary>
    public void AddSource(IDefinitionSource source) => _sources.Add(source);

    /// <summary>
    /// Re-loads every source and merges them in registration order (later sources override earlier).
    /// Updates <see cref="Current"/>. Safe to call in the background; unreachable sources are skipped.
    /// </summary>
    public async Task<WinButlerDefinitions> RefreshAsync(CancellationToken ct = default)
    {
        WinButlerDefinitions? merged = null;
        foreach (var source in _sources)
        {
            var defs = await source.LoadAsync(ct).ConfigureAwait(false);
            if (defs == null)
                continue;
            merged = merged == null ? defs : WinButlerDefinitions.Merge(merged, defs);
        }

        if (merged != null)
            Current = merged;
        return Current;
    }
}
