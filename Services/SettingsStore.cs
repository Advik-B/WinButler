using System;
using System.IO;
using System.Text.Json;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Persists the user's UI preferences to %APPDATA%\WinButler\settings.json across launches.
/// Deliberately stores ONLY the accent and target drive — <see cref="AppSettings.IsDryRun"/>
/// is never persisted, so every launch starts dry-run ON (the safety default; a past
/// dry-run-off session must never silently carry over). Corrupt/missing file → defaults,
/// never a throw.
/// </summary>
public static class SettingsStore
{
    /// <summary>Test seam: redirects the settings location; null = the default %APPDATA% path.</summary>
    internal static string? DirectoryOverride { get; set; }

    private static string Dir => DirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinButler");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>The persisted subset — note the absence of any dry-run field, by design.</summary>
    private sealed record Dto(string? Accent, string? TargetDrive);

    /// <summary>Applies saved preferences onto <paramref name="settings"/>. Never throws.</summary>
    public static void Load(AppSettings settings)
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (dto is null)
                return;
            if (Enum.TryParse<AccentKind>(dto.Accent, ignoreCase: true, out var accent))
                settings.Accent = accent;
            if (!string.IsNullOrWhiteSpace(dto.TargetDrive))
                settings.TargetDrive = dto.TargetDrive;
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable settings must never block startup — fall back to defaults.
            Log.Warn("settings", "Could not read settings; using defaults.", ex);
        }
    }

    /// <summary>Atomically writes the current preferences (tmp + move). Never throws.</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var dto = new Dto(settings.Accent.ToString(), settings.TargetDrive);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("settings", "Could not save settings.", ex);
        }
    }
}
