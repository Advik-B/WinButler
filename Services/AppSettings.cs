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

    /// <summary>The one chromatic accent. The app is green-only now; the field is kept so a
    /// stale "Red" in an existing settings.json still loads (it renders green regardless).</summary>
    [ObservableProperty]
    private AccentKind _accent = AccentKind.Green;
}
