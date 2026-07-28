using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Builds a <see cref="SystemCommand"/> that runs an embedded PowerShell script
/// (<c>Scripts/*.ps1</c>) via <c>-EncodedCommand</c>, entirely in memory — nothing is ever written
/// to disk. WinButler runs elevated (<c>requireAdministrator</c>); extracting a script to a
/// user-writable location (e.g. <c>%APPDATA%</c>) and then invoking it from that elevated process
/// would let any unprivileged process running as the user overwrite it first, so this avoids the
/// disk round-trip, and the privilege-escalation hole it opens, altogether.
/// </summary>
public static class EmbeddedScript
{
    private const string ResourceMarker = ".Scripts.";

    /// <summary>A bare identifier — no quotes, no whitespace, no newlines. See <see cref="RunCommand"/>.</summary>
    private static readonly Regex ModePattern = new(@"^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled);

    /// <summary>Builds the command for the named script (e.g. "RemoveGhostDevices.ps1", matched
    /// against <c>Scripts/*.ps1</c>). <see cref="SystemCommand.DisplayOverride"/> is set so the
    /// dry-run preview and the runner's "> ..." line show the script name instead of a base64 blob.
    /// <paramref name="mode"/>, if given, is assigned to <c>$Mode</c> ahead of the script body —
    /// letting one script back several actions (e.g. a read-only preview and the real thing)
    /// without a param()/-File invocation.
    /// <para>The mode is restricted to a bare identifier and validated here rather than being a
    /// free-form PowerShell statement: it can originate from <c>Scripts/scripts.json</c>, and a
    /// value carrying a quote or newline would inject arbitrary code into a process that always
    /// runs elevated. Alphanumerics-only makes escaping the single-quoted assignment impossible.</para></summary>
    public static SystemCommand RunCommand(string fileName, string? mode = null)
    {
        var body = mode is null ? ReadText(fileName) : $"$Mode = '{ValidMode(mode)}'\n" + ReadText(fileName);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(body));
        return new SystemCommand("powershell.exe", $"-NoProfile -EncodedCommand {encoded}",
            DisplayOverride: $"powershell.exe -File {fileName} (embedded)");
    }

    private static string ValidMode(string mode) =>
        ModePattern.IsMatch(mode)
            ? mode
            : throw new InvalidOperationException(
                $"Script mode '{mode}' is not a bare identifier (letters and digits only). " +
                "Modes are embedded in the script body, so anything else could inject PowerShell.");

    private static string ReadText(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        // Restricted to .ps1 on purpose: Scripts/ also holds scripts.json, and whatever this
        // returns gets executed. Keeping the executable lookup structurally unable to reach a
        // non-script means a slip in a caller's own validation can't turn into running data.
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase) >= 0
                                  && n.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                                  && n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded script '{fileName}' not found in assembly.");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
