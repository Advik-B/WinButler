using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services.Definitions;

/// <summary>
/// Loads the definitions JSON embedded in the assembly (Data/definitions.json). This is the
/// always-available baseline; it loads synchronously and never needs the network.
/// </summary>
public sealed class BundledDefinitionSource : IDefinitionSource
{
    public string Name => "bundled";

    public Task<WinButlerDefinitions?> LoadAsync(CancellationToken ct = default)
        => Task.FromResult<WinButlerDefinitions?>(Load());

    /// <summary>Synchronous load — used at startup since the resource is local and tiny.
    /// Throws on a missing or unparseable resource (see <see cref="TryLoad"/> for the
    /// fail-closed variant the startup provider uses).</summary>
    public static WinButlerDefinitions Load() => Parse(ReadEmbeddedJson());

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
            Log.Error("definitions", "Bundled definitions.json failed to load.", ex);
            return null;
        }
    }

    /// <summary>Parses definitions JSON (test seam for the malformed-input path).</summary>
    internal static WinButlerDefinitions Parse(string json) =>
        JsonSerializer.Deserialize<WinButlerDefinitions>(json, DefinitionsJson.Options)
            ?? throw new InvalidOperationException("Embedded definitions.json failed to parse.");

    private static string ReadEmbeddedJson()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("definitions.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded definitions.json not found in assembly.");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
