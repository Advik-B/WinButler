using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Models;
using WinButler.Services;

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

    public ObservableCollection<CategoryCardInfo> CategoryCards { get; } = new();

    /// <summary>Empty for now — no page currently reports completed actions back to the shell.
    /// The UI shows the mockup's "NO SIGNAL" empty state until that wiring exists.</summary>
    public ObservableCollection<string> SessionActivity { get; } = new();

    public bool HasActivity => SessionActivity.Count > 0;

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

        RebuildCategoryCards();
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

    [RelayCommand]
    private async Task CleanAllAsync()
    {
        await _cleanPage.CleanSelectedCommand.ExecuteAsync(null);
        await _devJunkPage.CleanSelectedCommand.ExecuteAsync(null);
        RaiseAll();
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

        AddCard("EL", "Electron Leftovers", _cleanPage.ElectronCategory?.TotalBytes ?? 0,
            $"{_cleanPage.ElectronCategory?.Items.Count ?? 0} old versions", maxReclaim, "electron");
        AddCard("TM", "Temp Files", _cleanPage.TempCategory?.TotalBytes ?? 0,
            $"{_cleanPage.TempCategory?.Items.Count ?? 0} locations", maxReclaim, "temp");
        AddCard("CA", "Cache Sweep", _cleanPage.CacheCategory?.TotalBytes ?? 0,
            $"{_cleanPage.CacheCategory?.Items.Count ?? 0} cache dirs", maxReclaim, "cache");
        AddCard("DV", "Dev Junk", _devJunkPage.Groups.Sum(g => g.Group.ReclaimableBytes),
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
