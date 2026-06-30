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

    public DefinitionsProvider()
    {
        // Bundled definitions are always available and load synchronously.
        _sources.Add(new BundledDefinitionSource());
        Current = BundledDefinitionSource.Load();
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
