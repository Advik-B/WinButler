using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;

namespace WinButler.ViewModels;

/// <summary>
/// The system-overview screen: aggregates totals already computed by the other pages (no new
/// scanning). The disk-usage bar is deliberately simplified to Reclaimable/Redirectable/Other-used/Free
/// — a precise System vs. Apps vs. Media breakdown would need its own slow directory walk
/// (see the Dev Junk scan-performance note) for a number that's cosmetic here, not actionable.
/// </summary>
public partial class DashboardPageViewModel : ViewModelBase
{
    private readonly CleanPageViewModel _cleanPage;
    private readonly RedirectPageViewModel _redirectPage;
    private readonly DevJunkPageViewModel _devJunkPage;
    private readonly Action<string> _navigate;
    private readonly DiskIndexService _diskIndex;

    public ObservableCollection<CategoryCardInfo> CategoryCards { get; } = new();

    /// <summary>Newest-first feed of completed clean/redirect/dev-junk runs this session, fed by
    /// <see cref="CleanupCompletedMessage"/> broadcasts. Empty (the "NO SIGNAL" state) until the
    /// first action runs.</summary>
    public ObservableCollection<ActivityEntry> SessionActivity { get; } = new();

    public bool HasActivity => SessionActivity.Count > 0;

    [ObservableProperty]
    private string _driveLabel = "C:";

    public DashboardPageViewModel(
        CleanPageViewModel cleanPage, RedirectPageViewModel redirectPage,
        DevJunkPageViewModel devJunkPage, Action<string> navigate, DiskIndexService diskIndex)
    {
        _cleanPage = cleanPage;
        _redirectPage = redirectPage;
        _devJunkPage = devJunkPage;
        _navigate = navigate;
        _diskIndex = diskIndex;

        foreach (var category in _cleanPage.Categories)
            category.PropertyChanged += OnChildChanged;
        _devJunkPage.Groups.CollectionChanged += (_, _) => RaiseAll();

        // CLEAN ALL delegates to the two pages' clean commands — stay disabled while either
        // page is mid-operation so it can't overlap (or double-fire) their work.
        _cleanPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CleanPageViewModel.IsBusy))
                CleanAllCommand.NotifyCanExecuteChanged();
        };
        _devJunkPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DevJunkPageViewModel.IsBusy))
                CleanAllCommand.NotifyCanExecuteChanged();
        };

        // Record completed clean/redirect/dev-junk runs into the Session Activity feed.
        WeakReferenceMessenger.Default.Register<DashboardPageViewModel, CleanupCompletedMessage>(
            this, static (r, m) => r.OnCleanupCompleted(m));

        RebuildCategoryCards();
    }

    private void OnCleanupCompleted(CleanupCompletedMessage m)
    {
        var (icon, label) = m.Action switch
        {
            CleanupAction.Clean => ("mdi-broom", "Clean"),
            CleanupAction.DevJunk => ("mdi-code-braces", "Dev Junk"),
            CleanupAction.Redirect => ("mdi-swap-horizontal", "Redirect"),
            _ => ("mdi-check", "Done"),
        };
        var size = SizeFormatter.Format(m.Bytes);
        var verb = m.Action == CleanupAction.Redirect ? "moved" : "reclaimed";
        var text = m.DryRun
            ? $"{label} · dry run — {size}, {m.Count} item(s)"
            : $"{label} · {size} {verb}, {m.Count} item(s)";
        var entry = new ActivityEntry(icon, text, m.Time.ToString("HH:mm"));

        // The send happens on the UI thread today, but marshal defensively in case a future caller
        // reports from a background continuation.
        Dispatcher.UIThread.Post(() =>
        {
            SessionActivity.Insert(0, entry);
            OnPropertyChanged(nameof(HasActivity));
        });
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        RebuildCategoryCards();
        OnPropertyChanged(nameof(ToReclaimNowText));
        OnPropertyChanged(nameof(ToRedirectText));
        OnPropertyChanged(nameof(DiskUsedText));
        OnPropertyChanged(nameof(DiskTotalText));
        OnPropertyChanged(nameof(ReclaimablePercent));
        OnPropertyChanged(nameof(RedirectableGb));
    }

    public long ReclaimNowBytes =>
        _cleanPage.Categories.Sum(c => c.TotalBytes) + _devJunkPage.Groups.Sum(g => g.Group.ReclaimableBytes);
    public string ToReclaimNowText => SizeFormatter.Format(ReclaimNowBytes);

    public long RedirectableBytes => _redirectPage.Candidates.Where(c => c.CanRedirect).Sum(c => c.SizeBytes);
    public string ToRedirectText => SizeFormatter.Format(RedirectableBytes);
    public string RedirectableGb => ToRedirectText;

    private static DriveInfo? SystemDrive =>
        DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"));

    public long DiskTotalBytes => SystemDrive?.TotalSize ?? 0;
    public long DiskUsedBytes => DiskTotalBytes - (SystemDrive?.AvailableFreeSpace ?? 0);
    public string DiskUsedText => SizeFormatter.Format(DiskUsedBytes);
    public string DiskTotalText => SizeFormatter.Format(DiskTotalBytes);
    public double ReclaimablePercent => DiskTotalBytes == 0 ? 0 : Math.Min(100, ReclaimNowBytes * 100.0 / DiskTotalBytes);
    public double UsedPercent => DiskTotalBytes == 0 ? 0 : Math.Min(100, DiskUsedBytes * 100.0 / DiskTotalBytes);

    // ── System / Apps / Media disk breakdown (derived from the shared index) ──────────────────
    private long _systemBytes, _appsBytes, _mediaBytes, _otherBytes, _freeBytes;

    [ObservableProperty] private bool _hasBreakdown;
    [ObservableProperty] private bool _isScanningDisk;
    [ObservableProperty] private string _diskStatus = "Reading disk usage…";

    // Segment weights for the proportional bar (star sizing spans the whole track).
    public GridLength SystemStar => Star(_systemBytes);
    public GridLength AppsStar => Star(_appsBytes);
    public GridLength MediaStar => Star(_mediaBytes);
    public GridLength OtherStar => Star(_otherBytes);
    public GridLength FreeStar => Star(_freeBytes);
    private static GridLength Star(long v) => new(Math.Max(0, v), GridUnitType.Star);

    public string SystemText => SizeFormatter.Format(_systemBytes);
    public string AppsText => SizeFormatter.Format(_appsBytes);
    public string MediaText => SizeFormatter.Format(_mediaBytes);
    public string OtherText => SizeFormatter.Format(_otherBytes);
    public string FreeText => SizeFormatter.Format(_freeBytes);

    /// <summary>Auto-fired once when the dashboard first appears: builds (or reuses) the shared index
    /// and derives the disk split. That same index then backs every other scan.</summary>
    [RelayCommand]
    private async Task LoadBreakdownAsync()
    {
        if (HasBreakdown || IsScanningDisk)
            return;
        await ComputeBreakdownAsync();
    }

    /// <summary>Re-derives the split after an explicit RE-SCAN — the index was just rebuilt, so this
    /// reuses it (no second MFT read).</summary>
    public async Task RefreshBreakdownAsync()
    {
        if (IsScanningDisk)
            return;
        await ComputeBreakdownAsync();
    }

    private async Task ComputeBreakdownAsync()
    {
        IsScanningDisk = true;
        try
        {
            // This auto-fires on the app's first paint — a failure here must degrade to a
            // status line, never crash the shell.
            await RunGuardedAsync(async () =>
            {
                var index = await _diskIndex.EnsureBuiltAsync(
                    DiskIndexService.SystemDrive, new Progress<string>(s => DiskStatus = s));
                var bd = index.ComputeBreakdown();
                _systemBytes = bd.System;
                _appsBytes = bd.Apps;
                _mediaBytes = bd.Media;
                _freeBytes = SystemDrive?.AvailableFreeSpace ?? 0;
                // Everything uncategorized (caches, user data, NTFS metadata drift) lands in "Other".
                _otherBytes = Math.Max(0, DiskUsedBytes - bd.System - bd.Apps - bd.Media);
                HasBreakdown = true;
                RaiseBreakdown();
            }, s => DiskStatus = s, "Disk breakdown failed");
        }
        finally
        {
            IsScanningDisk = false;
        }
    }

    private void RaiseBreakdown()
    {
        OnPropertyChanged(nameof(SystemStar)); OnPropertyChanged(nameof(AppsStar));
        OnPropertyChanged(nameof(MediaStar)); OnPropertyChanged(nameof(OtherStar));
        OnPropertyChanged(nameof(FreeStar));
        OnPropertyChanged(nameof(SystemText)); OnPropertyChanged(nameof(AppsText));
        OnPropertyChanged(nameof(MediaText)); OnPropertyChanged(nameof(OtherText));
        OnPropertyChanged(nameof(FreeText));
    }

    private bool CanCleanAll() => !_cleanPage.IsBusy && !_devJunkPage.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCleanAll))]
    private async Task CleanAllAsync()
    {
        // The child cores guard their own bodies; this outer guard only backstops the
        // aggregation itself (failures land in the log, the children own their status lines).
        await RunGuardedAsync(async () =>
        {
            // Capture both pages' selections up front so ONE confirm covers the whole sweep
            // (the child cores skip their own prompt when driven here).
            var cleanSelection = _cleanPage.SelectedTargets;
            var devGroups = _devJunkPage.SelectedGroups;
            var allTargets = cleanSelection.Concat(_devJunkPage.SelectedTargets).ToList();
            if (allTargets.Count == 0)
                return;

            if (!_cleanPage.IsDryRun &&
                !await ConfirmAsync(ConfirmRequest.ForDeletion(
                    $"Clean All — delete {allTargets.Count} item(s)?", allTargets)))
            {
                return;
            }

            await _cleanPage.CleanSelectedCoreAsync(cleanSelection);
            await _devJunkPage.CleanSelectedCoreAsync(devGroups);
            RaiseAll();
        }, _ => { }, "Clean All failed");
    }

    [RelayCommand]
    private void GoToRedirect() => _navigate("redirect");

    private void RebuildCategoryCards()
    {
        CategoryCards.Clear();
        var maxReclaim = new[]
        {
            _cleanPage.ElectronCategory?.TotalBytes ?? 0,
            _cleanPage.TempCategory?.TotalBytes ?? 0,
            _cleanPage.CacheCategory?.TotalBytes ?? 0,
            _devJunkPage.Groups.Sum(g => g.Group.ReclaimableBytes),
        }.DefaultIfEmpty(0).Max();
        if (maxReclaim == 0) maxReclaim = 1;

        AddCard("mdi-atom", "Electron Leftovers", _cleanPage.ElectronCategory?.TotalBytes ?? 0,
            $"{_cleanPage.ElectronCategory?.Items.Count ?? 0} old versions", maxReclaim, "electron");
        AddCard("mdi-timer-sand", "Temp Files", _cleanPage.TempCategory?.TotalBytes ?? 0,
            $"{_cleanPage.TempCategory?.Items.Count ?? 0} locations", maxReclaim, "temp");
        AddCard("mdi-cached", "Cache Sweep", _cleanPage.CacheCategory?.TotalBytes ?? 0,
            $"{_cleanPage.CacheCategory?.Items.Count ?? 0} cache dirs", maxReclaim, "cache");
        AddCard("mdi-code-braces", "Dev Junk", _devJunkPage.Groups.Sum(g => g.Group.ReclaimableBytes),
            $"{_devJunkPage.Groups.Count} toolchains", maxReclaim, "devjunk");
    }

    private void AddCard(string glyph, string title, long bytes, string countText, long maxReclaim, string navTag) =>
        CategoryCards.Add(new CategoryCardInfo(glyph, title, SizeFormatter.Format(bytes), countText,
            Math.Min(100, bytes * 100.0 / maxReclaim), new RelayCommand(() => _navigate(navTag))));
}

/// <summary>Plain data for one dashboard category card — display-only, no INPC needed since
/// the collection itself is rebuilt (not mutated in place) on every change.</summary>
public sealed record CategoryCardInfo(
    string IconGlyph, string Title, string SizeText, string CountText, double BarPercent,
    System.Windows.Input.ICommand ClickCommand);

/// <summary>One row in the Dashboard's Session Activity feed: an mdi icon key, a formatted summary,
/// and a short timestamp.</summary>
public sealed record ActivityEntry(string IconKey, string Text, string TimeText);
