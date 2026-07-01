using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace WinButler.ViewModels;

/// <summary>Marker so <see cref="WinButler.ViewLocator"/> resolves the dedicated Electron
/// Leftovers screen. Wraps the shared <see cref="CleanPageViewModel"/> (see
/// <see cref="TempPageViewModel"/> for the pattern) and additionally groups its Electron
/// category's flat item list by app for the card+expand layout.</summary>
public sealed class ElectronPageViewModel : ViewModelBase
{
    public CleanPageViewModel Clean { get; }
    public ObservableCollection<ElectronGroupViewModel> Groups { get; } = new();

    public ElectronPageViewModel(CleanPageViewModel clean)
    {
        Clean = clean;
        var category = clean.ElectronCategory;
        if (category is not null)
        {
            category.Items.CollectionChanged += (_, _) => RebuildGroups();
            RebuildGroups();
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        var category = Clean.ElectronCategory;
        if (category is null)
            return;

        foreach (var group in category.Items.GroupBy(i => i.GroupKey ?? i.DisplayName))
            Groups.Add(new ElectronGroupViewModel(group.Key, group.ToList()));
    }
}
