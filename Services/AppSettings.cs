using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// App-wide state shared by reference across pages so the global Dry-run toggle, the
/// chosen redirect drive, and the LED accent color are each a single source of truth.
/// </summary>
public partial class AppSettings : ObservableObject
{
    /// <summary>When true (default), all destructive actions only simulate.</summary>
    [ObservableProperty]
    private bool _isDryRun = true;

    /// <summary>Drive letter (e.g. "S") selected as the redirection target.</summary>
    [ObservableProperty]
    private string? _targetDrive;

    /// <summary>The one chromatic accent — LED red (default, matches the design mockup) or green.</summary>
    [ObservableProperty]
    private AccentKind _accent = AccentKind.Red;
}
