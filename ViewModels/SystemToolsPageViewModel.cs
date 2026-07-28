using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>
/// The System Tools page: one-click Windows maintenance (DISM component cleanup, SFC, Windows Update
/// cache flush, event-log clear, WMI reset). These run external tools rather than deleting files, so
/// they sit outside the scan/clean flow. Dry-run is honoured: with it on, a non-read-only action only
/// PRINTS the commands it would run and launches nothing; with it off, each destructive action routes
/// through the shell confirm modal first. Output streams live into a shared pane.
/// </summary>
public partial class SystemToolsPageViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly SystemActionRunner _runner;
    private readonly PrivacyCleaner _privacy;
    private CancellationTokenSource? _opCts;

    public ObservableCollection<SystemAction> Actions { get; }
    public ObservableCollection<SystemAction> AdvancedActions { get; }
    public ObservableCollection<PrivacyOp> PrivacyOps { get; }

    /// <summary>Live command output for the currently running (or last) action.</summary>
    public ObservableCollection<string> Output { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunPrivacyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Pick a maintenance action. Dry run prints what would run without doing it.";

    public bool IsDryRun => _settings.IsDryRun;

    /// <summary>Whether any Advanced action registered — the view's "ADVANCED" divider hides when
    /// none did (the script-backed ones come from a manifest that can legitimately be empty).
    /// Intentionally has no change notification: the catalog is built once in the constructor and
    /// never mutates, so the binding's single read at attach time is always correct.</summary>
    public bool HasAdvancedActions => AdvancedActions.Count > 0;

    /// <param name="scripts">Script-backed actions from <c>Scripts/scripts.json</c>; defaults to the
    /// bundled manifest (tests inject their own). Mirrors <see cref="KnownLocationsScanner"/>'s
    /// bundled-by-default convenience.</param>
    public SystemToolsPageViewModel(AppSettings settings, SystemActionRunner runner, PrivacyCleaner privacy,
        ScriptCatalog? scripts = null)
    {
        _settings = settings;
        _runner = runner;
        _privacy = privacy;

        // Built-in Windows-tool actions are defined in code (executable commands must never be
        // data-driven — see SystemCommand); script-backed ones are appended from the manifest.
        var catalog = BuildCatalog().Concat((scripts ?? ScriptCatalog.LoadBundled()).Actions);
        Actions = new ObservableCollection<SystemAction>();
        AdvancedActions = new ObservableCollection<SystemAction>();
        foreach (var a in catalog)
            (a.IsAdvanced ? AdvancedActions : Actions).Add(a);

        PrivacyOps = new ObservableCollection<PrivacyOp>
        {
            new("explorer", "Clear File Explorer history", "Recent files, jump lists, and the Run/address-bar history."),
            new("7zip", "Clear 7-Zip history", "7-Zip's folder history, shortcuts and last-opened panels."),
        };

        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.IsDryRun))
                OnPropertyChanged(nameof(IsDryRun));
        };
    }

    private bool CanRun() => !IsBusy;
    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _opCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunPrivacyAsync(PrivacyOp? op)
    {
        if (op is null)
            return;

        Output.Clear();
        var dryRun = _settings.IsDryRun;

        // Live run: confirm, and be explicit that registry entries are NOT recoverable (no Recycle Bin).
        if (!dryRun && !await ConfirmAsync(new ConfirmRequest(
                $"Clear: {op.Name}?", 0, 0,
                "Recent files go to the Recycle Bin, but registry history entries are removed permanently and cannot be restored.")))
        {
            StatusText = "Cancelled.";
            return;
        }

        IsBusy = true;
        StatusText = dryRun ? $"Checking {op.Name}…" : $"Clearing {op.Name}…";
        try
        {
            var progress = new Progress<string>(line => Output.Add(line));
            await RunGuardedAsync(async () =>
            {
                var result = await Task.Run(() => op.Id switch
                {
                    "explorer" => _privacy.ClearExplorerHistory(dryRun, progress),
                    "7zip" => _privacy.ClearSevenZipHistory(dryRun, progress),
                    _ => new PrivacyCleaner.Result(0, 0),
                });
                StatusText = result.Summarise(dryRun);
            }, s => StatusText = s, $"{op.Name} failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunActionAsync(SystemAction? action)
    {
        if (action is null)
            return;

        Output.Clear();

        // Dry-run: preview the commands and do nothing (read-only actions still run — they change
        // nothing, so there's real value in running them even while simulating).
        if (_settings.IsDryRun && !action.IsReadOnly)
        {
            Output.Add($"DRY RUN — {action.Name} would run:");
            foreach (var step in action.Steps)
                Output.Add($"> {step.Display}");
            Output.Add("Turn off Dry run to execute.");
            StatusText = $"Dry run — {action.Name} not executed.";
            return;
        }

        // Live destructive action: confirm first (read-only actions never prompt).
        if (!action.IsReadOnly)
        {
            var detail = string.IsNullOrEmpty(action.Warning) ? action.Description : action.Warning;
            if (!await ConfirmAsync(new ConfirmRequest($"Run: {action.Name}?", 0, 0, detail)))
            {
                StatusText = "Cancelled.";
                return;
            }
        }

        IsBusy = true;
        StatusText = $"Running {action.Name}…";
        ReportProgress($"{action.Name}…", null); // indeterminate — DISM/SFC take minutes
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            var progress = new Progress<string>(line => Output.Add(line));
            await RunGuardedAsync(async () =>
            {
                int exit = action.Id == "wu-cache"
                    ? await FlushWindowsUpdateCacheAsync(progress, cts.Token)
                    : await _runner.RunAsync(action.Steps, progress, cts.Token);

                StatusText = exit == 0
                    ? $"{action.Name} completed."
                    : $"{action.Name} finished with exit code {exit} — see output.";
                Log.Info("system-action", $"{action.Name} completed (exit {exit}).");
            }, s => StatusText = s, $"{action.Name} failed");
        }
        finally
        {
            ClearProgress();
            _opCts = null;
            IsBusy = false;
        }
    }

    /// <summary>Stops the Windows Update services, clears the download cache, and ALWAYS restarts the
    /// services — even on cancellation or failure — so the machine is never left with Update stopped.</summary>
    private async Task<int> FlushWindowsUpdateCacheAsync(IProgress<string> output, CancellationToken ct)
    {
        var stop = new[] { new SystemCommand("net.exe", "stop wuauserv"), new SystemCommand("net.exe", "stop bits") };
        var start = new[] { new SystemCommand("net.exe", "start wuauserv"), new SystemCommand("net.exe", "start bits") };

        try
        {
            // The STOP runs inside the try: if a cancel lands mid-stop it throws here, and the finally
            // still restarts the services. (If it threw outside the try, Update could stay disabled.)
            await _runner.RunAsync(stop, output, ct);

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var download = Path.Combine(windir, "SoftwareDistribution", "Download");
            ClearDirectoryContents(download, output, ct);
        }
        finally
        {
            // Never leave Windows Update stopped — restart is not cancellable. Starting an
            // already-running service is a harmless no-op, so this is safe on every path.
            output.Report("Restarting Windows Update services…");
            await _runner.RunAsync(start, output, CancellationToken.None);
        }
        return 0;
    }

    /// <summary>Deletes the contents of a directory (keeping the directory), skipping reparse points
    /// so a junction is unlinked rather than followed. Per-item failures are reported, not fatal.</summary>
    private static void ClearDirectoryContents(string dir, IProgress<string> output, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
        {
            output.Report($"(nothing to clear at {dir})");
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(entry))
                {
                    var info = new DirectoryInfo(entry);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        info.Delete(recursive: false); // unlink the junction, don't follow it
                    else
                        info.Delete(recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
                output.Report($"deleted {Path.GetFileName(entry)}");
            }
            catch (Exception ex)
            {
                output.Report($"skipped {Path.GetFileName(entry)} — {ex.Message}");
            }
        }
    }

    /// <summary>The built-in Windows-tool actions. These stay in code on purpose: they are literal
    /// executable + argument pairs, and <see cref="SystemCommand"/> must never be data-driven.
    /// Script-backed actions come from <c>Scripts/scripts.json</c> via <see cref="ScriptCatalog"/>.</summary>
    private static IReadOnlyList<SystemAction> BuildCatalog() => new[]
    {
        new SystemAction
        {
            Id = "analyze-store", Name = "Analyze component store", IsReadOnly = true,
            Description = "Report the WinSxS size and whether a cleanup is recommended. Changes nothing.",
            Steps = new[] { new SystemCommand("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore") },
        },
        new SystemAction
        {
            Id = "component-cleanup", Name = "Clean up component store",
            Description = "Remove superseded WinSxS components to reclaim disk space.",
            Warning = "Removes previous component versions. This can take several minutes and should not be interrupted.",
            Steps = new[] { new SystemCommand("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup") },
        },
        new SystemAction
        {
            Id = "sp-superseded", Name = "Remove superseded update backups",
            Description = "Delete service-pack/update backup files made redundant by installed updates.",
            Warning = "After this you can no longer uninstall the affected Windows updates.",
            Steps = new[] { new SystemCommand("dism.exe", "/Online /Cleanup-Image /SpSuperseded") },
        },
        new SystemAction
        {
            Id = "sfc", Name = "Run System File Checker",
            Description = "Scan protected system files and repair any that are corrupted.",
            Warning = "Runs sfc /scannow — this can take several minutes.",
            Steps = new[] { new SystemCommand("sfc.exe", "/scannow") },
        },
        new SystemAction
        {
            Id = "wu-cache", Name = "Flush Windows Update cache",
            Description = "Stop the Update services, clear the download cache, then restart them.",
            Warning = "Any pending update downloads are discarded and will be re-fetched.",
            Steps = new[]
            {
                new SystemCommand("net.exe", "stop wuauserv"),
                new SystemCommand("net.exe", "stop bits"),
                new SystemCommand("(clear)", @"%WinDir%\SoftwareDistribution\Download"),
                new SystemCommand("net.exe", "start wuauserv"),
                new SystemCommand("net.exe", "start bits"),
            },
        },
        new SystemAction
        {
            Id = "event-log-clear", Name = "Clear all event logs", IsAdvanced = true,
            Description = "Empty every Windows event log.",
            Warning = "Permanently clears ALL event logs. Diagnostic history is lost and cannot be recovered.",
            Steps = new[] { new SystemCommand("powershell.exe", "-NoProfile -Command \"wevtutil el | ForEach-Object { wevtutil cl $_ }\"") },
        },
        new SystemAction
        {
            Id = "wmi-reset", Name = "Reset WMI repository", IsAdvanced = true,
            Description = "Rebuild the Windows Management Instrumentation repository.",
            Warning = "Only use this if WMI is broken. A reset can break software that registered custom WMI classes.",
            Steps = new[] { new SystemCommand("winmgmt.exe", "/resetrepository") },
        },
    };
}
