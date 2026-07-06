using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services.Definitions;

/// <summary>
/// Loads the path-rule definitions embedded in the assembly. The rules live as several per-domain
/// JSON files under <c>Data/definitions/</c> (cache, redirect, known-location catalogs); this source
/// folds them all into one <see cref="WinButlerDefinitions"/>. It is the always-available baseline —
/// it loads synchronously and never needs the network.
/// </summary>
public sealed class BundledDefinitionSource : IDefinitionSource
{
    /// <summary>Marker that identifies our definition resources among all embedded resources.
    /// The folder <c>Data\definitions\</c> becomes <c>.definitions.</c> in the manifest name.</summary>
    private const string ResourceMarker = ".definitions.";

    public string Name => "bundled";

    public Task<WinButlerDefinitions?> LoadAsync(CancellationToken ct = default)
        => Task.FromResult<WinButlerDefinitions?>(Load());

    /// <summary>Synchronous load — used at startup since the resources are local and tiny. Folds
    /// every embedded definitions file together (merge order is by resource name, so results are
    /// deterministic). Throws if no files are found or ANY file is unparseable — a partial load
    /// could drop the deny-list, so it is all-or-nothing (see <see cref="TryLoad"/> for the
    /// fail-closed variant the startup provider uses).</summary>
    public static WinButlerDefinitions Load()
    {
        WinButlerDefinitions? merged = null;
        foreach (var (shortName, json) in ReadEmbeddedJsonFiles())
        {
            WinButlerDefinitions parsed;
            try
            {
                parsed = Parse(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Embedded definitions file '{shortName}' failed to parse.", ex);
            }

            merged = merged is null ? parsed : WinButlerDefinitions.Merge(merged, parsed);
        }

        // ReadEmbeddedJsonFiles throws when there are zero files, so merged is never null here.
        return merged!;
    }

    /// <summary>Fail-soft load for startup: returns null (logged) instead of throwing, so a bad
    /// bundled edit can be handled gracefully rather than crashing the app.</summary>
    public static WinButlerDefinitions? TryLoad()
    {
        try
        {
            return Load();
        }
        catch (Exception ex)
        {
            Log.Error("definitions", "Bundled definitions failed to load.", ex);
            return null;
        }
    }

    /// <summary>Parses one definitions file's JSON (test seam for the malformed-input path).</summary>
    internal static WinButlerDefinitions Parse(string json) =>
        JsonSerializer.Deserialize<WinButlerDefinitions>(json, DefinitionsJson.Options)
            ?? throw new InvalidOperationException("Embedded definitions JSON failed to parse.");

    /// <summary>Reads every embedded definitions file, ordered by resource name for a deterministic
    /// merge. Yields the short file name (for error messages) and its raw JSON.</summary>
    private static IEnumerable<(string ShortName, string Json)> ReadEmbeddedJsonFiles()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames()
            .Where(n => n.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
            throw new InvalidOperationException("No embedded definitions files found in assembly.");

        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (ShortName(name), reader.ReadToEnd());
        }
    }

    /// <summary>Trims a full manifest resource name down to the file name for error messages,
    /// e.g. <c>WinButler.Data.definitions.cache.json</c> → <c>cache.json</c>.</summary>
    private static string ShortName(string resourceName)
    {
        var markerEnd = resourceName.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase)
                        + ResourceMarker.Length;
        return markerEnd < resourceName.Length ? resourceName[markerEnd..] : resourceName;
    }
}
