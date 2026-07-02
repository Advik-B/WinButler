using System;
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
/// The Dev Junk screen: per-tool cards combining on-disk size, the safely-reclaimable subset
/// (via <see cref="DevJunkAggregator"/>), and a "Redirect →" shortcut into the existing
/// Redirect flow. Deletion still goes through the shared <see cref="ICleaner"/> chokepoint —
/// this page adds aggregation, not a new delete path.
/// </summary>
public partial class DevJunkPageViewModel : ViewModelBase
{
    private readonly DevJunkAggregator _aggregator;
    private readonly AppSettings _settings;
    private readonly ICleaner _cleaner;
    private readonly RedirectPageViewModel _redirectPage;
    private readonly System.Action<string> _navigate;
    private readonly DiskIndexService _diskIndex;

    public ObservableCollection<DevToolGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>The in-flight operation's cancellation source; null when idle.</summary>
    private CancellationTokenSource? _opCts;

    [ObservableProperty]
    private string _statusText = "Ready. Click Scan to find dev-tool space.";

    public DevJunkPageViewModel(
        DevJunkAggregator aggregator, AppSettings settings, ICleaner cleaner,
        RedirectPageViewModel redirectPage, System.Action<string> navigate, DiskIndexService diskIndex)
    {
        _aggregator = aggregator;
        _settings = settings;
        _cleaner = cleaner;
        _redirectPage = redirectPage;
        _navigate = navigate;
        _diskIndex = diskIndex;
    }

    public long SelectedBytes => Groups.Where(g => g.IsSelected).Sum(g => g.Group.ReclaimableBytes);
    public string SelectedSummary => $"{Groups.Count(g => g.IsSelected)} selected · {SizeFormatter.Format(SelectedBytes)}";

    private bool CanRun() => !IsBusy;

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _opCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning dev-tool folders…";
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                // Build (or reuse) the shared volume index so the reclaimable-subset sizing reads from it.
                await _diskIndex.EnsureBuiltAsync(DiskIndexService.SystemDrive, new Progress<string>(s => StatusText = s), cts.Token);

                // Reuse the Redirect screen's own scan — it already sizes every dev-tool root,
                // so running it again here would pay that (expensive) cost twice.
                if (_redirectPage.Candidates.Count == 0)
                    await _redirectPage.ScanCommand.ExecuteAsync(null);

                var candidates = _redirectPage.Candidates.Select(c => c.Candidate).ToList();
                var groups = await _aggregator.BuildAsync(candidates, cts.Token);

                Groups.Clear();
                foreach (var g in groups)
                {
                    var vm = new DevToolGroupViewModel(g);
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(DevToolGroupViewModel.IsSelected))
                            RaiseTotals();
                    };
                    Groups.Add(vm);
                }
                RaiseTotals();

                var total = groups.Sum(g => g.ReclaimableBytes);
                StatusText = $"Scan complete — {SizeFormatter.Format(total)} reclaimable across {groups.Count} tool(s).";
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
        var selected = Groups.Where(g => g.IsSelected && g.IsSelectable).ToList();
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

                foreach (var group in selected)
                {
                    // Soft-stop between groups (never mid-delete) so the partial summary,
                    // invalidation and activity broadcast below still happen on cancellation.
                    if (cts.Token.IsCancellationRequested)
                        break;

                    bool groupOk = true;
                    foreach (var target in group.Group.ReclaimableTargets)
                    {
                        var result = await _cleaner.CleanAsync(target, dryRun);
                        if (result.Succeeded) { reclaimed += result.BytesReclaimed; ok++; }
                        else { failed++; groupOk = false; }
                    }
                    if (!dryRun && groupOk)
                    {
                        group.Cleaned = true;
                        group.IsSelected = false;
                    }
                }

                // Real deletions happened — the shared index is stale for these paths now.
                if (!dryRun)
                    _diskIndex.Invalidate(DiskIndexService.SystemDrive);

                StatusText = dryRun
                    ? $"DRY RUN — nothing was deleted. Would reclaim {SizeFormatter.Format(reclaimed)} from {ok} item(s)."
                    : $"Cleaned {ok} item(s), reclaimed {SizeFormatter.Format(reclaimed)}." +
                      (failed > 0 ? $" {failed} skipped (in use / access denied)." : "");
                if (cts.Token.IsCancellationRequested)
                    StatusText = "Cancelled — " + StatusText;

                WeakReferenceMessenger.Default.Send(
                    new CleanupCompletedMessage(CleanupAction.DevJunk, reclaimed, ok, dryRun, System.DateTime.Now));
            }, s => StatusText = s, "Clean failed");
        }
        finally
        {
            _opCts = null;
            IsBusy = false;
        }
    }

    /// <summary>Pre-selects the matching candidate on the Redirect screen, then switches to
    /// it — reuses the existing redirect flow instead of duplicating it here.</summary>
    [RelayCommand]
    private void GoToRedirect(DevToolGroupViewModel group)
    {
        var match = _redirectPage.Candidates.FirstOrDefault(c => c.Candidate.TargetName == group.Group.TargetName);
        if (match is { CanRedirect: true })
            match.IsSelected = true;
        _navigate("redirect");
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedSummary));
    }
}
