using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Definitions;
using WinButler.Services.Mft;

namespace WinButler.ViewModels;

/// <summary>
/// Application shell. Hosts the navigation pages and owns the app-wide
/// <see cref="AppSettings"/> (dry-run, target drive, accent) shared with every page.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public AppSettings Settings { get; }

    public CleanPageViewModel CleanPage { get; }
    public RedirectPageViewModel RedirectPage { get; }
    public DiskScannerPageViewModel DiskPage { get; }

    /// <summary>Electron/Temp/Cache each get a dedicated screen, but all three share the one
    /// CleanPageViewModel scan coordinator underneath (a single RE-SCAN drives everything).</summary>
    public ElectronPageViewModel ElectronPage { get; }
    public TempPageViewModel TempPage { get; }
    public CachePageViewModel CachePage { get; }

    public DashboardPageViewModel DashboardPage { get; }
    public DevJunkPageViewModel DevJunkPage { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _activeNavTag = "dashboard";

    /// <summary>Non-null when the rule definitions failed to load; shown as a persistent banner.
    /// Cleaning is disabled in this state (no scanners were constructed).</summary>
    [ObservableProperty]
    private string? _definitionsError;

    public bool HasDefinitionsError => DefinitionsError is not null;

    /// <summary>The single toast overlay slot (bottom-center), auto-dismissed after ~3.6s.</summary>
    [ObservableProperty]
    private ToastViewModel? _currentToast;

    /// <summary>The single destructive-action confirm modal slot; null when no confirm is pending.</summary>
    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    /// <summary>True while RE-SCAN runs, so double-clicking it can't overlap two sweeps.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanAllCommand))]
    private bool _isRescanning;

    private DispatcherTimer? _toastTimer;

    /// <summary>The one shared whole-volume disk index, wired into the sizing chokepoint at
    /// construction so every scan reuses a single MFT read instead of re-walking the filesystem.</summary>
    private readonly DiskIndexService _diskIndex;

    /// <summary>Path-rule definitions (bundled now; can be refreshed from online sources later).</summary>
    public DefinitionsProvider Definitions { get; }

    public MainWindowViewModel() : this(new DefinitionsProvider()) { }

    /// <summary>Test seam: inject a provider (e.g. a fail-closed one) without touching startup.</summary>
    internal MainWindowViewModel(DefinitionsProvider definitions)
    {
        Settings = new AppSettings();
        Definitions = definitions;
        var defs = Definitions.Current;

        // One shared whole-volume index behind every scan (Clean/Redirect/Dev Junk/Dashboard/Disk
        // Explorer) so none re-walks the filesystem. Wire it into the sizing chokepoint that every
        // scanner already funnels through, then hand it to the pages that trigger scans.
        var diskScan = new DiskScanService();
        _diskIndex = new DiskIndexService(diskScan);
        DirectorySizeCalculator.Index = _diskIndex;

        // Fail closed on a bad definitions load: an empty ruleset means an empty deny-list, so
        // scanning against it would offer everything (incl. credential-shaped paths) for deletion.
        // Construct NO scanners and surface a persistent error instead of scanning unsafely.
        DefinitionsError = Definitions.LoadFailed
            ? "Rule definitions failed to load — cleaning is disabled to stay safe. See the log."
            : null;

        // One shared rule engine so the deny-list is enforced identically on every scanner.
        var safeCaches = new SafeCaches(defs.Cache);
        IScanner[] scanners = Definitions.LoadFailed
            ? Array.Empty<IScanner>()
            : new IScanner[]
            {
                new ElectronLeftoverScanner(safeCaches),
                new TempScanner(safeCaches),
                new CacheScanner(safeCaches),
            };
        CleanPage = new CleanPageViewModel(Settings, scanners, new Cleaner(), _diskIndex);
        RedirectPage = new RedirectPageViewModel(Settings, new RedirectionService(defs.Redirect), _diskIndex);
        DiskPage = new DiskScannerPageViewModel(diskScan, _diskIndex);

        ElectronPage = new ElectronPageViewModel(CleanPage);
        TempPage = new TempPageViewModel(CleanPage);
        CachePage = new CachePageViewModel(CleanPage);

        var devJunkAggregator = new DevJunkAggregator(safeCaches);
        DevJunkPage = new DevJunkPageViewModel(devJunkAggregator, Settings, new Cleaner(), RedirectPage, Navigate, _diskIndex);

        DashboardPage = new DashboardPageViewModel(CleanPage, RedirectPage, DevJunkPage, Navigate, _diskIndex);

        // Route every page's destructive-confirm through the shell's modal slot. Dashboard
        // CLEAN ALL confirms once itself, so its children skip their own prompt when it drives them.
        CleanPage.ConfirmInteraction = ConfirmViaModalAsync;
        RedirectPage.ConfirmInteraction = ConfirmViaModalAsync;
        DevJunkPage.ConfirmInteraction = ConfirmViaModalAsync;
        DashboardPage.ConfirmInteraction = ConfirmViaModalAsync;

        // Surface every completed clean/redirect run as a toast (the Dashboard's activity
        // feed subscribes to the same broadcast).
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, CleanupCompletedMessage>(
            this, static (r, m) => r.OnCleanupCompleted(m));

        _currentPage = DashboardPage;
    }

    private void OnCleanupCompleted(CleanupCompletedMessage m)
    {
        var size = SizeFormatter.Format(m.Bytes);
        var text = m.DryRun
            ? $"Dry run — would reclaim {size} ({m.Count} item(s))"
            : m.Action == CleanupAction.Redirect
                ? $"Moved {size} ({m.Count} folder(s))"
                : $"Reclaimed {size} ({m.Count} item(s))";

        // The send happens on the UI thread today; marshal defensively like the Dashboard does.
        Dispatcher.UIThread.Post(() => ShowToast(text, m.DryRun ? ToastKind.Dry : ToastKind.Ok));
    }

    /// <summary>Switches the visible page. Bound directly from the sidebar's nav buttons
    /// and the Scan menu's per-cleaner shortcuts.</summary>
    [RelayCommand]
    public void Navigate(string? tag)
    {
        ActiveNavTag = tag ?? "dashboard";
        CurrentPage = tag switch
        {
            "electron" => ElectronPage,
            "temp" => TempPage,
            "cache" => CachePage,
            "devjunk" => DevJunkPage,
            "redirect" => RedirectPage,
            "disk" => DiskPage,
            _ => DashboardPage,
        };
    }

    private bool CanRescanAll() => !IsRescanning;

    /// <summary>File/Scan menu + toolbar "RE-SCAN": re-runs every scanner-backed page at once.</summary>
    [RelayCommand(CanExecute = nameof(CanRescanAll))]
    private async Task RescanAllAsync()
    {
        IsRescanning = true;
        try
        {
            // The page scans guard their own bodies and own their status lines; this outer guard
            // only backstops the orchestration (failures land in the log).
            await RunGuardedAsync(async () =>
            {
                // Explicit refresh: drop the cached index so the first page scan rebuilds it once from
                // disk, then the rest reuse that fresh build.
                _diskIndex.Invalidate(DiskIndexService.SystemDrive);
                // ExecuteAsync ignores CanExecute, so check it — a page mid-scan is already doing
                // this work and its collections must not be mutated by a second overlapping run.
                if (CleanPage.ScanCommand.CanExecute(null))
                    await CleanPage.ScanCommand.ExecuteAsync(null);
                if (RedirectPage.ScanCommand.CanExecute(null))
                    await RedirectPage.ScanCommand.ExecuteAsync(null);
                // Index is freshly rebuilt now — re-derive the disk breakdown from it (no extra MFT read).
                await DashboardPage.RefreshBreakdownAsync();
            }, _ => { }, "Rescan All failed");
        }
        finally
        {
            IsRescanning = false;
        }
    }

    [RelayCommand]
    private void SetAccent(string? kind)
    {
        Settings.Accent = kind == "green" ? AccentKind.Green : AccentKind.Red;
    }

    [RelayCommand]
    private void ToggleDryRun() => Settings.IsDryRun = !Settings.IsDryRun;

    [RelayCommand]
    private static void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Shows a toast for ~3.6s, replacing whatever is currently shown (matches the
    /// mockup's single-toast-at-a-time behavior — it resets the dismiss timer on Show).</summary>
    public void ShowToast(string message, ToastKind kind)
    {
        _toastTimer?.Stop();
        CurrentToast = new ToastViewModel(message, kind);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3600) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            CurrentToast = null;
        };
        _toastTimer.Start();
    }

    /// <summary>Opens the single destructive-confirm modal slot. Only one page can have a
    /// pending confirm at a time, matching the mockup's single `confirm` state.</summary>
    public void RequestConfirm(string title, int count, long bytes, Action onConfirmed)
    {
        PendingConfirm = new ConfirmDialogViewModel(title, count, bytes,
            onConfirmed: () => { PendingConfirm = null; onConfirmed(); },
            onCancelled: () => PendingConfirm = null);
    }

    /// <summary>Awaitable confirm used by page ViewModels: shows the modal and completes with
    /// the user's choice. Wired into each page's <see cref="ViewModelBase.ConfirmInteraction"/>.</summary>
    private Task<bool> ConfirmViaModalAsync(ConfirmRequest request)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingConfirm = new ConfirmDialogViewModel(
            request.Title, request.Count, request.Bytes,
            onConfirmed: () => { PendingConfirm = null; tcs.TrySetResult(true); },
            onCancelled: () => { PendingConfirm = null; tcs.TrySetResult(false); },
            detail: request.Detail);
        return tcs.Task;
    }
}
