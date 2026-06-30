using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services.Definitions;

/// <summary>
/// A source of path-rule definitions. Implementations include the bundled JSON shipped with the
/// app and (in future) remote JSON fetched over HTTP. Sources are layered by
/// <see cref="DefinitionsProvider"/>, later ones overriding/extending earlier ones.
/// </summary>
public interface IDefinitionSource
{
    /// <summary>A short name for diagnostics (e.g. "bundled", "github").</summary>
    string Name { get; }

    /// <summary>Loads definitions, or returns null if unavailable (e.g. offline). Must not throw.</summary>
    Task<WinButlerDefinitions?> LoadAsync(CancellationToken ct = default);
}
