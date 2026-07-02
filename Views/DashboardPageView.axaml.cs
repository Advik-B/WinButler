using Avalonia.Controls;
using WinButler.ViewModels;

namespace WinButler.Views;

public partial class DashboardPageView : UserControl
{
    public DashboardPageView()
    {
        InitializeComponent();
        // Derive the disk breakdown the first time the dashboard is shown (builds/warms the shared
        // index). The command self-guards against re-running once loaded.
        Loaded += (_, _) =>
        {
            if (DataContext is DashboardPageViewModel vm && vm.LoadBreakdownCommand.CanExecute(null))
                vm.LoadBreakdownCommand.Execute(null);
        };
    }
}
