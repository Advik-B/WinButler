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
/// The Redirect page: moves large dev directories to another drive behind a junction, and
/// restores them. Dry-run and target drive come from shared <see cref="AppSettings"/>.
/// </summary>
public partial class RedirectPageViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IRedirectionService _service;
    private readonly DiskIndexService _diskIndex;

    public ObservableCollection<RedirectCandidateViewModel> Candidates { get; } = new();
    public ObservableCollection<RedirectRecord> ActiveRedirects { get; } = new();
    public ObservableCollection<string> Drives { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedirectSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>The in-flight operation's cancellation source; null when idle.</summary>
    private CancellationTokenSource? _opCts;

    [ObservableProperty]
    private string _statusText = "Pick a drive and Scan to find space to reclaim.";

    public RedirectPageViewModel(AppSettings settings, IRedirectionService service, DiskIndexService diskIndex)
    {
        _settings = settings;
        _service = service;
        _diskIndex = diskIndex;

        foreach (var d in _service.GetEligibleDrives())
            Drives.Add(d);

        _settings.TargetDrive ??= _service.SuggestTargetDrive() ?? Drives.FirstOrDefault();
        RefreshActive();
    }

    /// <summary>Two-way bound to the drive picker; stored in shared settings.</summary>
    public string? SelectedDrive
    {
        get => _settings.TargetDrive;
        set { if (_settings.TargetDrive != value) { _settings.TargetDrive = value; OnPropertyChanged(); } }
    }

    public bool IsDryRun => _settings.IsDryRun;

    private bool CanRun() => !IsBusy;

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _opCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning redirectable folders…";
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                // Build (or reuse) the shared volume index first; candidate sizing then reads from it.
                await _diskIndex.EnsureBuiltAsync(DiskIndexService.SystemDrive, new Progress<string>(s => StatusText = s), cts.Token);

                var found = await _service.ScanCandidatesAsync(cts.Token);
                Candidates.Clear();
                foreach (var c in found)
                    Candidates.Add(new RedirectCandidateViewModel(c));
                RefreshActive();

                var redirectable = found.Where(c => !c.IsAlreadyRedirected).Sum(c => c.SizeBytes);
                StatusText = $"Found {SizeFormatter.Format(redirectable)} redirectable across {found.Count} folder(s).";

                // Crash-recovery check: relocated data with no ledger record is invisible to Undo.
                var orphans = await Task.Run(() => _service.FindOrphanedRedirects());
                if (orphans.Count > 0)
                {
                    StatusText += $"  ⚠ {orphans.Count} orphaned folder(s) found in _redirected " +
                                  "with no ledger record — data preserved, see the log.";
                }
            }, s => StatusText = s, "Scan failed");
        }
        finally
        {
            _opCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RedirectSelectedAsync()
    {
        var drive = _settings.TargetDrive;
        if (string.IsNullOrEmpty(drive))
        {
            StatusText = "Select a target drive first.";
            return;
        }

        var selected = Candidates.Where(c => c.IsSelected && c.CanRedirect).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        IsBusy = true;
        var dryRun = _settings.IsDryRun;
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                long moved = 0;
                int ok = 0, failed = 0;
                string? lastMessage = null;

                foreach (var c in selected)
                {
                    if (cts.Token.IsCancellationRequested)
                        break;
                    try
                    {
                        // The token reaches robocopy: a mid-copy cancel kills the copy and the
                        // service removes the partial dest (original untouched). Catch it here
                        // so the folders that DID finish still get summarized below.
                        var result = await _service.RedirectAsync(c.Candidate, drive, dryRun, cts.Token);
                        lastMessage = result.Message;
                        if (result.Succeeded) { moved += result.BytesMoved; ok++; }
                        else failed++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                StatusText = dryRun
                    ? $"DRY RUN — would move {SizeFormatter.Format(moved)} from {ok} folder(s) to {drive}:. Nothing changed."
                    : $"Redirected {ok} folder(s), {SizeFormatter.Format(moved)} moved to {drive}:." +
                      (failed > 0 ? $" {failed} failed — {lastMessage}" : "");
                if (cts.Token.IsCancellationRequested)
                    StatusText = "Cancelled — " + StatusText;

                if (!dryRun)
                {
                    // Data moved off C: behind a junction — the index is now stale for those paths.
                    _diskIndex.Invalidate(DiskIndexService.SystemDrive);
                    await ScanAsync();
                }

                WeakReferenceMessenger.Default.Send(
                    new CleanupCompletedMessage(CleanupAction.Redirect, moved, ok, dryRun, DateTime.Now));
            }, s => StatusText = s, "Redirect failed");
        }
        finally
        {
            _opCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UndoAsync(RedirectRecord? record)
    {
        if (record == null)
            return;

        IsBusy = true;
        var dryRun = _settings.IsDryRun;
        using var cts = new CancellationTokenSource();
        _opCts = cts;
        try
        {
            await RunGuardedAsync(async () =>
            {
                var result = await _service.UndoAsync(record, dryRun, cts.Token);
                StatusText = result.Message;
                if (!dryRun && result.Succeeded)
                {
                    // Data moved back onto C: — refresh the index before rescanning.
                    _diskIndex.Invalidate(DiskIndexService.SystemDrive);
                    await ScanAsync();
                }
            }, s => StatusText = s, "Undo failed");
        }
        finally
        {
            _opCts = null;
            IsBusy = false;
        }
    }

    public bool HasActiveRedirects => ActiveRedirects.Count > 0;

    private void RefreshActive()
    {
        ActiveRedirects.Clear();
        foreach (var r in _service.GetActiveRedirects())
            ActiveRedirects.Add(r);
        OnPropertyChanged(nameof(HasActiveRedirects));
    }
}
