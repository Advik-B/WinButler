using Avalonia.Controls;

namespace WinButler.Views;

public partial class DashboardPageView : UserControl
{
    public DashboardPageView()
    {
        InitializeComponent();
        // No scan is triggered here: the used/free bar reads DriveInfo live, and the shell runs
        // the full scan once on launch (MainWindow.Opened).
    }
}
