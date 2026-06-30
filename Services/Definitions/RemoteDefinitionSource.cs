using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services.Definitions;

/// <summary>
/// Fetches definitions from a remote JSON URL (e.g. a raw GitHub file) so the app can pick up the
/// latest safe paths without an update. Failures (offline, 404, bad JSON) return null so the app
/// silently falls back to whatever other sources provide. Not enabled by default — register it via
/// <see cref="DefinitionsProvider.AddSource"/> when you want online updates.
/// </summary>
/// <example>
/// provider.AddSource(new RemoteDefinitionSource(
///     "https://raw.githubusercontent.com/&lt;you&gt;/winbutler-defs/main/definitions.json"));
/// await provider.RefreshAsync();
/// </example>
public sealed class RemoteDefinitionSource : IDefinitionSource
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _url;

    public RemoteDefinitionSource(string url) => _url = url;

    public string Name => "remote";

    public async Task<WinButlerDefinitions?> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(_url, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WinButlerDefinitions>(json, DefinitionsJson.Options);
        }
        catch
        {
            // Offline / unreachable / malformed → fall back to other sources.
            return null;
        }
    }
}
