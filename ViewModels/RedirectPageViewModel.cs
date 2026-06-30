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
/// The Redirect page: moves large dev directories to another drive behind a junction, and
/// restores them. Dry-run and target drive come from shared <see cref="AppSettings"/>.
/// </summary>
public partial class RedirectPageViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IRedirectionService _service;

    public ObservableCollection<RedirectCandidateViewModel> Candidates { get; } = new();
    public ObservableCollection<RedirectRecord> ActiveRedirects { get; } = new();
    public ObservableCollection<string> Drives { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedirectSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Pick a drive and Scan to find space to reclaim.";

    public RedirectPageViewModel(AppSettings settings, IRedirectionService service)
    {
        _settings = settings;
        _service = service;

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

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning redirectable folders…";
        try
        {
            var found = await _service.ScanCandidatesAsync();
            Candidates.Clear();
            foreach (var c in found)
                Candidates.Add(new RedirectCandidateViewModel(c));
            RefreshActive();

            var redirectable = found.Where(c => !c.IsAlreadyRedirected).Sum(c => c.SizeBytes);
            StatusText = $"Found {SizeFormatter.Format(redirectable)} redirectable across {found.Count} folder(s).";
        }
        finally
        {
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
        try
        {
            long moved = 0;
            int ok = 0, failed = 0;
            string? lastMessage = null;

            foreach (var c in selected)
            {
                var result = await _service.RedirectAsync(c.Candidate, drive, dryRun);
                lastMessage = result.Message;
                if (result.Succeeded) { moved += result.BytesMoved; ok++; }
                else failed++;
            }

            StatusText = dryRun
                ? $"DRY RUN — would move {SizeFormatter.Format(moved)} from {ok} folder(s) to {drive}:. Nothing changed."
                : $"Redirected {ok} folder(s), {SizeFormatter.Format(moved)} moved to {drive}:." +
                  (failed > 0 ? $" {failed} failed — {lastMessage}" : "");

            if (!dryRun)
                await ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UndoAsync(RedirectRecord? record)
    {
        if (record == null)
            return;

        IsBusy = true;
        var dryRun = _settings.IsDryRun;
        try
        {
            var result = await _service.UndoAsync(record, dryRun);
            StatusText = result.Message;
            if (!dryRun && result.Succeeded)
                await ScanAsync();
        }
        finally
        {
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
