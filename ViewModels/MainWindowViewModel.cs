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
    public AppsPageViewModel AppsPage { get; }
    public SteamPageViewModel SteamPage { get; }

    public DashboardPageViewModel DashboardPage { get; }
    public DevJunkPageViewModel DevJunkPage { get; }
    public SystemToolsPageViewModel SystemToolsPage { get; }

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

    // ── Status-bar progress (the one shell-wide progress slot: MFT parse, scans, deletes) ──
    /// <summary>True while a long operation is running; shows the status-bar progress region.</summary>
    [ObservableProperty]
    private bool _isProgressActive;

    /// <summary>Indeterminate (spinner) vs. determinate (0..1) — MFT parse/scan spin; deletes fill.</summary>
    [ObservableProperty]
    private bool _isProgressIndeterminate;

    /// <summary>Determinate progress fraction, 0..1 (ignored while indeterminate).</summary>
    [ObservableProperty]
    private double _progressValue;

    /// <summary>The active operation's status line, shown beside the bar.</summary>
    [ObservableProperty]
    private string _progressText = "";

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
                new KnownLocationsScanner(safeCaches, defs.KnownLocations),
                new SteamScanner(safeCaches, new Services.Steam.SteamLocator()),
            };
        CleanPage = new CleanPageViewModel(Settings, scanners, new Cleaner(), _diskIndex);
        RedirectPage = new RedirectPageViewModel(Settings, new RedirectionService(defs.Redirect), _diskIndex);
        DiskPage = new DiskScannerPageViewModel(diskScan, _diskIndex);

        ElectronPage = new ElectronPageViewModel(CleanPage);
        TempPage = new TempPageViewModel(CleanPage);
        CachePage = new CachePageViewModel(CleanPage);
        AppsPage = new AppsPageViewModel(CleanPage);
        SteamPage = new SteamPageViewModel(CleanPage);

        var devJunkAggregator = new DevJunkAggregator(safeCaches);
        DevJunkPage = new DevJunkPageViewModel(devJunkAggregator, Settings, new Cleaner(), RedirectPage, Navigate, _diskIndex);

        SystemToolsPage = new SystemToolsPageViewModel(Settings, new SystemActionRunner(), new PrivacyCleaner());

        DashboardPage = new DashboardPageViewModel(CleanPage, RedirectPage, DevJunkPage, Navigate);

        // Route every page's destructive-confirm through the shell's modal slot. Dashboard
        // CLEAN ALL confirms once itself, so its children skip their own prompt when it drives them.
        CleanPage.ConfirmInteraction = ConfirmViaModalAsync;
        RedirectPage.ConfirmInteraction = ConfirmViaModalAsync;
        DevJunkPage.ConfirmInteraction = ConfirmViaModalAsync;
        DashboardPage.ConfirmInteraction = ConfirmViaModalAsync;
        SystemToolsPage.ConfirmInteraction = ConfirmViaModalAsync;

        // Route every page's long-op progress into the shell's single status-bar progress slot.
        CleanPage.ShellProgress = SetProgress;
        RedirectPage.ShellProgress = SetProgress;
        DevJunkPage.ShellProgress = SetProgress;
        DiskPage.ShellProgress = SetProgress;
        SystemToolsPage.ShellProgress = SetProgress;

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
            "apps" => AppsPage,
            "steam" => SteamPage,
            "devjunk" => DevJunkPage,
            "redirect" => RedirectPage,
            "disk" => DiskPage,
            "system" => SystemToolsPage,
            _ => DashboardPage,
        };
    }

    private bool CanRescanAll() => !IsRescanning;

    /// <summary>Runs every scanner-backed page at once — fired once on launch (MainWindow.Opened,
    /// the "scan by default" behavior) and by the status-bar RE-SCAN button. Builds the shared
    /// index up front so the slow MFT parse streams into the status-bar progress bar; the page
    /// scans then reuse that one build.</summary>
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
                SetProgress("Reading disk…", null); // indeterminate while the MFT is parsed
                // Drop the cached index so this build reads fresh from disk; the page scans reuse it.
                _diskIndex.Invalidate(DiskIndexService.SystemDrive);
                // Build the one shared index here (not lazily inside the first page scan) so the
                // "Parsing MFT — X / Y records" progress lands in the shell's status bar.
                await _diskIndex.EnsureBuiltAsync(
                    DiskIndexService.SystemDrive, new Progress<string>(s => ProgressText = s));

                SetProgress("Scanning for reclaimable space…", null);
                // ExecuteAsync ignores CanExecute, so check it — a page mid-scan is already doing
                // this work and its collections must not be mutated by a second overlapping run.
                if (CleanPage.ScanCommand.CanExecute(null))
                    await CleanPage.ScanCommand.ExecuteAsync(null);
                if (RedirectPage.ScanCommand.CanExecute(null))
                    await RedirectPage.ScanCommand.ExecuteAsync(null);
                // Dev Junk piggybacks on the redirect candidates, so it must run after Redirect.
                if (DevJunkPage.ScanCommand.CanExecute(null))
                    await DevJunkPage.ScanCommand.ExecuteAsync(null);

                DashboardPage.Refresh();
            }, _ => { }, "Rescan All failed");
        }
        finally
        {
            SetProgress(null, null);
            IsRescanning = false;
        }
    }

    /// <summary>Drives the single status-bar progress slot. <paramref name="text"/> null hides the
    /// bar; <paramref name="fraction"/> null shows an indeterminate spinner, otherwise a 0..1 bar.
    /// Wired into every page's <see cref="ViewModelBase.ShellProgress"/>.</summary>
    public void SetProgress(string? text, double? fraction)
    {
        if (text is null)
        {
            IsProgressActive = false;
            ProgressText = "";
            return;
        }
        IsProgressActive = true;
        ProgressText = text;
        IsProgressIndeterminate = fraction is null;
        ProgressValue = fraction ?? 0;
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
            detail: request.Detail,
            isDestructive: request.IsDestructive);
        return tcs.Task;
    }
}
