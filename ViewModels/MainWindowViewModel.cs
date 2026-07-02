using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>The single toast overlay slot (bottom-center), auto-dismissed after ~3.6s.</summary>
    [ObservableProperty]
    private ToastViewModel? _currentToast;

    /// <summary>The single destructive-action confirm modal slot; null when no confirm is pending.</summary>
    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    private DispatcherTimer? _toastTimer;

    /// <summary>The one shared whole-volume disk index, wired into the sizing chokepoint at
    /// construction so every scan reuses a single MFT read instead of re-walking the filesystem.</summary>
    private readonly DiskIndexService _diskIndex;

    /// <summary>Path-rule definitions (bundled now; can be refreshed from online sources later).</summary>
    public DefinitionsProvider Definitions { get; }

    public MainWindowViewModel()
    {
        Settings = new AppSettings();
        Definitions = new DefinitionsProvider();
        var defs = Definitions.Current;

        // One shared whole-volume index behind every scan (Clean/Redirect/Dev Junk/Dashboard/Disk
        // Explorer) so none re-walks the filesystem. Wire it into the sizing chokepoint that every
        // scanner already funnels through, then hand it to the pages that trigger scans.
        var diskScan = new DiskScanService();
        _diskIndex = new DiskIndexService(diskScan);
        DirectorySizeCalculator.Index = _diskIndex;

        IScanner[] scanners =
        {
            new ElectronLeftoverScanner(),
            new TempScanner(),
            new CacheScanner(new SafeCaches(defs.Cache)),
        };
        CleanPage = new CleanPageViewModel(Settings, scanners, new Cleaner(), _diskIndex);
        RedirectPage = new RedirectPageViewModel(Settings, new RedirectionService(defs.Redirect), _diskIndex);
        DiskPage = new DiskScannerPageViewModel(diskScan, _diskIndex);

        ElectronPage = new ElectronPageViewModel(CleanPage);
        TempPage = new TempPageViewModel(CleanPage);
        CachePage = new CachePageViewModel(CleanPage);

        var devJunkAggregator = new DevJunkAggregator(new SafeCaches(defs.Cache));
        DevJunkPage = new DevJunkPageViewModel(devJunkAggregator, Settings, new Cleaner(), RedirectPage, Navigate, _diskIndex);

        DashboardPage = new DashboardPageViewModel(CleanPage, RedirectPage, DevJunkPage, Navigate, _diskIndex);

        _currentPage = DashboardPage;
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

    /// <summary>File/Scan menu + toolbar "RE-SCAN": re-runs every scanner-backed page at once.</summary>
    [RelayCommand]
    private async Task RescanAllAsync()
    {
        // Explicit refresh: drop the cached index so the first page scan rebuilds it once from disk,
        // then the rest reuse that fresh build.
        _diskIndex.Invalidate(DiskIndexService.SystemDrive);
        await CleanPage.ScanCommand.ExecuteAsync(null);
        await RedirectPage.ScanCommand.ExecuteAsync(null);
        // Index is freshly rebuilt now — re-derive the disk breakdown from it (no extra MFT read).
        await DashboardPage.RefreshBreakdownAsync();
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
}
