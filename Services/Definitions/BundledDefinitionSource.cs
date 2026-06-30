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

    /// <summary>Synchronous load — used at startup since the resource is local and tiny.</summary>
    public static WinButlerDefinitions Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("definitions.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded definitions.json not found in assembly.");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<WinButlerDefinitions>(json, DefinitionsJson.Options)
            ?? throw new InvalidOperationException("Embedded definitions.json failed to parse.");
    }
}
