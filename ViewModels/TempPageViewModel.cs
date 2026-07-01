namespace WinButler.ViewModels;

/// <summary>
/// Marker so <see cref="WinButler.ViewLocator"/> resolves the dedicated Temp Files screen
/// (TempPageView) instead of the combined Clean screen. Wraps the shared
/// <see cref="CleanPageViewModel"/> scan coordinator — Electron/Temp/Cache all scan together,
/// only the screen shown for each is dedicated. See <see cref="ElectronPageViewModel"/> and
/// <see cref="CachePageViewModel"/> for the siblings.
/// </summary>
public sealed class TempPageViewModel : ViewModelBase
{
    public CleanPageViewModel Clean { get; }

    public TempPageViewModel(CleanPageViewModel clean) => Clean = clean;
}
