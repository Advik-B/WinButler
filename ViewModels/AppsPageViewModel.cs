namespace WinButler.ViewModels;

/// <summary>Marker so <see cref="WinButler.ViewLocator"/> resolves the dedicated App &amp; Game
/// Leftovers screen (the known-locations catalog). Wraps the shared <see cref="CleanPageViewModel"/>
/// scan coordinator — see <see cref="CachePageViewModel"/> for the pattern.</summary>
public sealed class AppsPageViewModel : ViewModelBase
{
    public CleanPageViewModel Clean { get; }

    public AppsPageViewModel(CleanPageViewModel clean) => Clean = clean;
}
