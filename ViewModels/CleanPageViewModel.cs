using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;

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
    private readonly DiskIndexService _diskIndex;

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>The in-flight operation's cancellation source; null when idle.</summary>
    private CancellationTokenSource? _opCts;

    [ObservableProperty]
    private string _statusText = "Ready. Click Scan to find reclaimable space.";

    [ObservableProperty]
    private bool _hasScanned;

    public CleanPageViewModel(AppSettings settings, IReadOnlyList<IScanner> scanners, ICleaner cleaner, DiskIndexService diskIndex)
    {
        _settings = settings;
        _scanners = scanners;
        _cleaner = cleaner;
        _diskIndex = diskIndex;

        foreach (var scanner in _scanners)
            Categories.Add(new CategoryViewModel(scanner.Title, scanner.Category, UpdateTotals));
    }

    /// <summary>Dedicated lookups so the per-category screens (Electron/Temp/Cache) can bind
    /// directly to "their" category without the view needing to filter <see cref="Categories"/> itself.</summary>
    public CategoryViewModel? ElectronCategory => Categories.FirstOrDefault(c => c.Category == CleanupCategory.ElectronLeftover);
    public CategoryViewModel? TempCategory => Categories.FirstOrDefault(c => c.Category == CleanupCategory.Temp);
    public CategoryViewModel? CacheCategory => Categories.FirstOrDefault(c => c.Category == CleanupCategory.Cache);

    public long SelectedBytes => Categories.Sum(c => c.SelectedBytes);

    public string SelectedSummary =>
        $"{SizeFormatter.Format(SelectedBytes)} selected across "
        + $"{Categories.Sum(c => c.SelectedItems.Count())} item(s)";

    private bool CanRun() => !IsBusy;

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _opCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning…";
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                // Build (or reuse) the one shared volume index first; the scanners' size lookups then hit it.
                await _diskIndex.EnsureBuiltAsync(DiskIndexService.SystemDrive, new Progress<string>(s => StatusText = s), cts.Token);

                var tasks = _scanners.Select(s => s.ScanAsync(cts.Token)).ToArray();
                var resultsPerScanner = await Task.WhenAll(tasks);

                for (int i = 0; i < _scanners.Count; i++)
                    Categories[i].SetItems(resultsPerScanner[i]);

                HasScanned = true;
                UpdateTotals();

                var total = Categories.Sum(c => c.TotalBytes);
                StatusText = $"Scan complete — {SizeFormatter.Format(total)} reclaimable found.";
            }, s => StatusText = s, "Scan failed");
        }
        finally
        {
            _opCts = null;
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
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                long reclaimed = 0;
                int ok = 0, failed = 0;

                foreach (var item in selected)
                {
                    // Soft-stop between items (never mid-delete) so the partial summary,
                    // rescan and activity broadcast below still happen on cancellation.
                    if (cts.Token.IsCancellationRequested)
                        break;

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
                    // Files were deleted for real — drop the stale index so the rescan reflects it.
                    _diskIndex.Invalidate(DiskIndexService.SystemDrive);
                    await ScanAsync();
                }

                if (cts.Token.IsCancellationRequested)
                    StatusText = "Cancelled — " + StatusText;

                // Report the run to the Dashboard's Session Activity feed (dry runs included, tagged).
                WeakReferenceMessenger.Default.Send(
                    new CleanupCompletedMessage(CleanupAction.Clean, reclaimed, ok, dryRun, DateTime.Now));
            }, s => StatusText = s, "Clean failed");
        }
        finally
        {
            _opCts = null;
            IsBusy = false;
        }
    }

    private void UpdateTotals()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedSummary));
    }
}
