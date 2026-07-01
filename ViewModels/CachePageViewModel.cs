namespace WinButler.ViewModels;

/// <summary>Marker so <see cref="WinButler.ViewLocator"/> resolves the dedicated Cache Sweep
/// screen — see <see cref="TempPageViewModel"/> for the pattern.</summary>
public sealed class CachePageViewModel : ViewModelBase
{
    public CleanPageViewModel Clean { get; }

    public CachePageViewModel(CleanPageViewModel clean) => Clean = clean;
}
