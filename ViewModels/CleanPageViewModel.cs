using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>
/// The Clean page: scans for reclaimable space and deletes the selected targets through
/// the <see cref="ICleaner"/> chokepoint. Dry-run state comes from shared <see cref="AppSettings"/>.
/// </summary>
public partial class CleanPageViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IScanner> _scanners;
    private readonly ICleaner _cleaner;
    private readonly AppSettings _settings;

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready. Click Scan to find reclaimable space.";

    [ObservableProperty]
    private bool _hasScanned;

    public CleanPageViewModel(AppSettings settings, IReadOnlyList<IScanner> scanners, ICleaner cleaner)
    {
        _settings = settings;
        _scanners = scanners;
        _cleaner = cleaner;

        foreach (var scanner in _scanners)
            Categories.Add(new CategoryViewModel(scanner.Title, scanner.Category, UpdateTotals));
    }

    public long SelectedBytes => Categories.Sum(c => c.SelectedBytes);

    public string SelectedSummary =>
        $"{SizeFormatter.Format(SelectedBytes)} selected across "
        + $"{Categories.Sum(c => c.SelectedItems.Count())} item(s)";

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning…";
        try
        {
            var tasks = _scanners.Select(s => s.ScanAsync()).ToArray();
            var resultsPerScanner = await Task.WhenAll(tasks);

            for (int i = 0; i < _scanners.Count; i++)
                Categories[i].SetItems(resultsPerScanner[i]);

            HasScanned = true;
            UpdateTotals();

            var total = Categories.Sum(c => c.TotalBytes);
            StatusText = $"Scan complete — {SizeFormatter.Format(total)} reclaimable found.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanSelectedAsync()
    {
        var selected = Categories.SelectMany(c => c.SelectedItems).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        IsBusy = true;
        var dryRun = _settings.IsDryRun;
        StatusText = dryRun ? "Simulating clean…" : "Cleaning…";
        try
        {
            long reclaimed = 0;
            int ok = 0, failed = 0;

            foreach (var item in selected)
            {
                var result = await _cleaner.CleanAsync(item.Target, dryRun);
                if (result.Succeeded)
                {
                    reclaimed += result.BytesReclaimed;
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            if (dryRun)
            {
                StatusText =
                    $"DRY RUN — nothing was deleted. Would reclaim {SizeFormatter.Format(reclaimed)} " +
                    $"from {ok} item(s). Turn off Dry run to delete for real.";
            }
            else
            {
                StatusText =
                    $"Cleaned {ok} item(s), reclaimed {SizeFormatter.Format(reclaimed)}." +
                    (failed > 0 ? $" {failed} skipped (in use / access denied)." : "");
                await ScanAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateTotals()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedSummary));
    }
}
