using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>
/// The system-overview screen: aggregates totals already computed by the other pages (it does no
/// scanning of its own). The disk-usage bar is a simple used/free split read from
/// <see cref="DriveInfo"/>, so it renders instantly without touching the MFT.
/// </summary>
public partial class DashboardPageViewModel : ViewModelBase
{
    private readonly CleanPageViewModel _cleanPage;
    private readonly RedirectPageViewModel _redirectPage;
    private readonly DevJunkPageViewModel _devJunkPage;
    private readonly Action<string> _navigate;

    public ObservableCollection<CategoryCardInfo> CategoryCards { get; } = new();

    [ObservableProperty]
    private string _driveLabel = "C:";

    public DashboardPageViewModel(
        CleanPageViewModel cleanPage, RedirectPageViewModel redirectPage,
        DevJunkPageViewModel devJunkPage, Action<string> navigate)
    {
        _cleanPage = cleanPage;
        _redirectPage = redirectPage;
        _devJunkPage = devJunkPage;
        _navigate = navigate;

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

        RebuildCategoryCards();
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    /// <summary>Re-reads the derived totals (used/free bar, reclaim/redirect figures, category
    /// cards) after the shell's RE-SCAN completes. Child collection-changed events cover in-page
    /// edits; this covers the cross-page sweep, which touches redirect candidates too.</summary>
    public void Refresh() => RaiseAll();

    private void RaiseAll()
    {
        RebuildCategoryCards();
        OnPropertyChanged(nameof(ToReclaimNowText));
        OnPropertyChanged(nameof(ToRedirectText));
        OnPropertyChanged(nameof(DiskUsedText));
        OnPropertyChanged(nameof(DiskTotalText));
        OnPropertyChanged(nameof(DiskFreeText));
        OnPropertyChanged(nameof(ReclaimablePercent));
        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(UsedStar));
        OnPropertyChanged(nameof(FreeStar));
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
    public long DiskFreeBytes => SystemDrive?.AvailableFreeSpace ?? 0;
    public string DiskUsedText => SizeFormatter.Format(DiskUsedBytes);
    public string DiskTotalText => SizeFormatter.Format(DiskTotalBytes);
    public string DiskFreeText => SizeFormatter.Format(DiskFreeBytes);
    public double ReclaimablePercent => DiskTotalBytes == 0 ? 0 : Math.Min(100, ReclaimNowBytes * 100.0 / DiskTotalBytes);
    public double UsedPercent => DiskTotalBytes == 0 ? 0 : Math.Min(100, DiskUsedBytes * 100.0 / DiskTotalBytes);

    // Proportional weights for the two-part used/free bar (star sizing spans the whole track).
    public GridLength UsedStar => new(Math.Max(0, DiskUsedBytes), GridUnitType.Star);
    public GridLength FreeStar => new(Math.Max(0, DiskFreeBytes), GridUnitType.Star);

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
