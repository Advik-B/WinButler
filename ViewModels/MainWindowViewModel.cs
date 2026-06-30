using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Services;
using WinButler.Services.Definitions;
using WinButler.Services.Mft;

namespace WinButler.ViewModels;

/// <summary>
/// Application shell. Hosts the navigation pages and owns the app-wide
/// <see cref="AppSettings"/> (dry-run, target drive) shared with every page.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public AppSettings Settings { get; }

    public CleanPageViewModel CleanPage { get; }
    public RedirectPageViewModel RedirectPage { get; }
    public DiskScannerPageViewModel DiskPage { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    /// <summary>Path-rule definitions (bundled now; can be refreshed from online sources later).</summary>
    public DefinitionsProvider Definitions { get; }

    public MainWindowViewModel()
    {
        Settings = new AppSettings();
        Definitions = new DefinitionsProvider();
        var defs = Definitions.Current;

        IScanner[] scanners =
        {
            new ElectronLeftoverScanner(),
            new TempScanner(),
            new CacheScanner(new SafeCaches(defs.Cache)),
        };
        CleanPage = new CleanPageViewModel(Settings, scanners, new Cleaner());
        RedirectPage = new RedirectPageViewModel(Settings, new RedirectionService(defs.Redirect));
        DiskPage = new DiskScannerPageViewModel(new DiskScanService());

        _currentPage = CleanPage;
    }

    /// <summary>Switches the visible page. Called from the NavigationView selection handler.</summary>
    public void Navigate(string? tag)
    {
        CurrentPage = tag switch
        {
            "redirect" => RedirectPage,
            "disk" => DiskPage,
            _ => CleanPage,
        };
    }
}
