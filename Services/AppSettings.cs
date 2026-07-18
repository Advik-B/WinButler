using CommunityToolkit.Mvvm.ComponentModel;

namespace WinButler.Services;

/// <summary>
/// App-wide state shared by reference across pages so the global Dry-run toggle and the
/// chosen redirect drive are each a single source of truth.
/// </summary>
public partial class AppSettings : ObservableObject
{
    /// <summary>When true (default), all destructive actions only simulate.</summary>
    [ObservableProperty]
    private bool _isDryRun = true;

    /// <summary>Drive letter (e.g. "S") selected as the redirection target.</summary>
    [ObservableProperty]
    private string? _targetDrive;
}
