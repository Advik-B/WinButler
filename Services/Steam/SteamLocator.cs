using System;
using Microsoft.Win32;

namespace WinButler.Services.Steam;

/// <summary>
/// Discovers Steam's install directory from the registry (<c>HKCU\Software\Valve\Steam\SteamPath</c>),
/// the same key the batch script reads. Returns null when Steam isn't installed. The registry read is
/// injectable so tests can drive the parsing/scanning without a real Steam install.
/// </summary>
public sealed class SteamLocator
{
    private readonly Func<string?> _readSteamPath;

    public SteamLocator() : this(ReadSteamPathFromRegistry) { }

    /// <summary>Test seam: supply the raw SteamPath value (return null to simulate "not installed").</summary>
    public SteamLocator(Func<string?> readSteamPath) => _readSteamPath = readSteamPath;

    /// <summary>The Steam install directory (e.g. <c>C:\Program Files (x86)\Steam</c>), or null if
    /// Steam isn't installed or the value is unreadable. Forward slashes are normalised to back.</summary>
    public string? FindSteamPath()
    {
        string? raw;
        try { raw = _readSteamPath(); }
        catch { return null; }

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Replace('/', '\\').TrimEnd('\\');
    }

    private static string? ReadSteamPathFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }
}
