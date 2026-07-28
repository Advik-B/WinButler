using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinButler.Models;
using WinButler.Services.Definitions;

namespace WinButler.Services;

/// <summary>
/// Builds the script-backed half of the System Tools catalog from the embedded
/// <c>Scripts/scripts.json</c> manifest, so adding an action is a drop-in: add a <c>.ps1</c> under
/// <c>Scripts/</c>, add one entry, done — no code change. See <c>Scripts/README.md</c>.
/// <para><b>Why this is not part of <see cref="WinButlerDefinitions"/>.</b> That type is what
/// <see cref="Definitions.DefinitionsProvider.AddSource"/> /
/// <see cref="Definitions.DefinitionsProvider.RefreshAsync"/> merge overlays into, including from
/// <see cref="Definitions.RemoteDefinitionSource"/> (an unauthenticated URL fetch). Anything
/// reachable from there is remotely overridable by construction, and this app runs elevated. The
/// action catalog is loaded from its own embedded resource, outside that merge path, so a future
/// remote-definitions rollout can never reach it.</para>
/// <para>The manifest still only ever <em>names</em> a script that shipped inside the assembly and
/// a bare-identifier mode — never a command line — so the executable surface stays fixed at compile
/// time either way.</para>
/// </summary>
public sealed class ScriptCatalog
{
    /// <summary>The manifest resource, as it appears in the assembly manifest.</summary>
    private const string ResourceSuffix = ".Scripts.scripts.json";

    /// <summary>A plain script file name — no directory separators, no traversal.</summary>
    private static readonly Regex ScriptNamePattern = new(@"^[A-Za-z0-9._-]+\.ps1$", RegexOptions.Compiled);

    public IReadOnlyList<SystemAction> Actions { get; }

    private ScriptCatalog(IReadOnlyList<SystemAction> actions) => Actions = actions;

    /// <summary>A catalog with no script actions — what <see cref="LoadBundled"/> yields when the
    /// manifest is unusable. Exposed for tests that need that state without a broken manifest.</summary>
    internal static ScriptCatalog Empty => new(Array.Empty<SystemAction>());

    /// <summary>Loads the bundled manifest, returning an EMPTY catalog (logged) if anything at all is
    /// wrong with it. Callers keep their code-defined actions, so the page still works — an empty
    /// action catalog is useless but, unlike an empty deny-list, not dangerous.</summary>
    public static ScriptCatalog LoadBundled()
    {
        try
        {
            return new ScriptCatalog(Parse(ReadManifest()));
        }
        catch (Exception ex)
        {
            Log.Error("script-catalog", "scripts.json failed to load — no script actions registered.", ex);
            return new ScriptCatalog(Array.Empty<SystemAction>());
        }
    }

    /// <summary>Parses and validates a manifest (test seam). Throws on the first problem: the load is
    /// all-or-nothing, so a bad entry can't leave a destructive action registered while the read-only
    /// preview that makes it safe to use silently goes missing.</summary>
    internal static IReadOnlyList<SystemAction> Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<ScriptActionManifest>(json, DefinitionsJson.Options)
            ?? throw new InvalidOperationException("scripts.json parsed to null.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actions = new List<SystemAction>();

        foreach (var entry in manifest.Actions)
        {
            Require(!string.IsNullOrWhiteSpace(entry.Id), "an entry is missing 'id'.");
            Require(seen.Add(entry.Id), $"'{entry.Id}' is declared more than once.");
            Require(!string.IsNullOrWhiteSpace(entry.Name), $"'{entry.Id}' is missing 'name'.");
            Require(!string.IsNullOrWhiteSpace(entry.Description), $"'{entry.Id}' is missing 'description'.");
            Require(ScriptNamePattern.IsMatch(entry.Script), $"'{entry.Id}' has an invalid 'script' name.");

            // A destructive action's warning is what the confirm modal shows; make stating the risk
            // mandatory rather than letting it silently fall back to the softer description.
            Require(entry.IsReadOnly || !string.IsNullOrWhiteSpace(entry.Warning),
                $"'{entry.Id}' is not read-only, so it must declare a 'warning'.");

            // Throws if the script isn't embedded, or if the mode isn't a bare identifier.
            var step = EmbeddedScript.RunCommand(entry.Script, entry.Mode);

            actions.Add(new SystemAction
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                Warning = entry.Warning,
                IsReadOnly = entry.IsReadOnly,
                IsAdvanced = entry.IsAdvanced,
                Steps = new[] { step },
            });
        }

        return actions;
    }

    private static void Require(bool condition, string problem)
    {
        if (!condition)
            throw new InvalidOperationException($"scripts.json: {problem}");
    }

    private static string ReadManifest()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded scripts.json not found in assembly.");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
