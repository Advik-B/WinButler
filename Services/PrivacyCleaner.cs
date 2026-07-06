using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinButler.Services.Privacy;

namespace WinButler.Services;

/// <summary>
/// Clears privacy/MRU traces: File Explorer recent items + address-bar/Run history, and 7-Zip's
/// folder history. Recent-item FILES go to the Recycle Bin (recoverable); REGISTRY values are removed
/// permanently (the registry has no Recycle Bin) — the UI's confirm makes that explicit. Dry-run
/// counts what would be removed and touches nothing.
/// </summary>
public sealed class PrivacyCleaner
{
    /// <summary>Outcome of a privacy operation: how many files and registry values were affected.</summary>
    public sealed record Result(int FilesRemoved, int RegistryValuesRemoved)
    {
        public string Summarise(bool dryRun)
        {
            var verb = dryRun ? "Would remove" : "Removed";
            return $"{verb} {FilesRemoved} recent item(s) and {RegistryValuesRemoved} registry value(s).";
        }
    }

    private const string RunMru = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private const string TypedPaths = @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";
    private const string SevenZipFm = @"Software\7-Zip\FM";
    private static readonly string[] SevenZipValues = { "FolderHistory", "FolderShortcuts", "PanelPath0", "PanelPath1" };

    private readonly IRegistryEditor _registry;

    public PrivacyCleaner(IRegistryEditor registry) => _registry = registry;
    public PrivacyCleaner() : this(new HkcuRegistryEditor()) { }

    /// <summary>Clears File Explorer history: recent-item files (Recycle Bin) plus the RunMRU and
    /// TypedPaths registry lists (permanent).</summary>
    public Result ClearExplorerHistory(bool dryRun, IProgress<string> log)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var recent = Path.Combine(appData, "Microsoft", "Windows", "Recent");

        int files = 0;
        foreach (var folder in new[]
                 {
                     recent,
                     Path.Combine(recent, "AutomaticDestinations"),
                     Path.Combine(recent, "CustomDestinations"),
                 })
            files += ClearFolderFiles(folder, dryRun, log);

        int values = ClearAllValues(RunMru, dryRun, log) + ClearAllValues(TypedPaths, dryRun, log);
        return new Result(files, values);
    }

    /// <summary>Clears 7-Zip's file-manager history (folder history, shortcuts, last panels).</summary>
    public Result ClearSevenZipHistory(bool dryRun, IProgress<string> log)
    {
        var present = _registry.GetValueNames(SevenZipFm);
        int values = 0;
        foreach (var name in SevenZipValues)
        {
            if (!present.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (dryRun)
            {
                log.Report($"would remove 7-Zip\\FM\\{name}");
            }
            else
            {
                _registry.DeleteValue(SevenZipFm, name);
                Log.Info("privacy", $"Removed registry value 7-Zip\\FM\\{name}");
                log.Report($"removed 7-Zip\\FM\\{name}");
            }
            values++;
        }
        return new Result(0, values);
    }

    private int ClearAllValues(string subKey, bool dryRun, IProgress<string> log)
    {
        var names = _registry.GetValueNames(subKey);
        var leaf = subKey[(subKey.LastIndexOf('\\') + 1)..];
        int count = 0;
        foreach (var name in names)
        {
            var shown = string.IsNullOrEmpty(name) ? "(default)" : name;
            if (dryRun)
            {
                log.Report($"would remove {leaf}\\{shown}");
            }
            else
            {
                _registry.DeleteValue(subKey, name);
                log.Report($"removed {leaf}\\{shown}");
            }
            count++;
        }
        if (!dryRun && count > 0)
            Log.Info("privacy", $"Cleared {count} value(s) under HKCU\\{subKey}");
        return count;
    }

    private static int ClearFolderFiles(string folder, bool dryRun, IProgress<string> log)
    {
        if (!Directory.Exists(folder))
            return 0;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder); }
        catch { return 0; }

        int count = 0;
        foreach (var file in files)
        {
            if (dryRun)
            {
                count++;
                continue;
            }
            try
            {
                RecycleBin.Send(file); // recoverable
                count++;
            }
            catch (Exception ex)
            {
                log.Report($"skipped {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        var name = Path.GetFileName(folder.TrimEnd('\\'));
        log.Report(dryRun ? $"would recycle {count} item(s) from {name}" : $"recycled {count} item(s) from {name}");
        return count;
    }
}
