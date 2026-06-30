using System.Linq;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using WinButler.ViewModels;

namespace WinButler.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Select the first nav item so the Clean page is highlighted on launch.
        Nav.SelectedItem = Nav.MenuItems.OfType<FANavigationViewItem>().FirstOrDefault();
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.SelectedItemContainer is FANavigationViewItem item)
            vm.Navigate(item.Tag as string);
    }
}
