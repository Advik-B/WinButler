using System;
using CommunityToolkit.Mvvm.Input;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>The destructive-action confirm modal (permanent-deletion warning). The shell
/// hosts one instance; a page requests it via <see cref="MainWindowViewModel.RequestConfirm"/>
/// and gets the answer through <paramref name="onConfirmed"/> — no page owns dialog state
/// itself, matching the "single overlay slot" the mockup's `confirm` state implies.</summary>
public sealed partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly Action _onConfirmed;
    private readonly Action _onCancelled;

    public string Title { get; }
    public int Count { get; }
    public string BytesText { get; }

    /// <summary>Optional one-line breakdown (e.g. "7 deleted permanently · 3 to Recycle Bin").</summary>
    public string? Detail { get; }
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public ConfirmDialogViewModel(string title, int count, long bytes, Action onConfirmed, Action onCancelled,
        string? detail = null)
    {
        Title = title;
        Count = count;
        BytesText = SizeFormatter.Format(bytes);
        Detail = detail;
        _onConfirmed = onConfirmed;
        _onCancelled = onCancelled;
    }

    [RelayCommand]
    private void Confirm() => _onConfirmed();

    [RelayCommand]
    private void Cancel() => _onCancelled();
}
