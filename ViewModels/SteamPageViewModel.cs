namespace WinButler.ViewModels;

/// <summary>Marker so <see cref="WinButler.ViewLocator"/> resolves the dedicated Steam Junk screen.
/// Wraps the shared <see cref="CleanPageViewModel"/> scan coordinator — see
/// <see cref="CachePageViewModel"/> for the pattern.</summary>
public sealed class SteamPageViewModel : ViewModelBase
{
    public CleanPageViewModel Clean { get; }

    public SteamPageViewModel(CleanPageViewModel clean) => Clean = clean;
}
