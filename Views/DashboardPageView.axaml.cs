using Avalonia.Controls;

namespace WinButler.Views;

public partial class DashboardPageView : UserControl
{
    // Below this Disk Hero card width, the two stat callouts reflow *beneath* the disk bar rather
    // than docking to its right (see the `Border.narrow` style in the .axaml). Tuned so side-by-side
    // is the default at the 1180px window and stacks only once the window is dragged narrow.
    // Deliberately distinct from the page-wide Responsive.NarrowUnder="820" in the .axaml: that
    // one gates the card grid on PAGE width, this one gates the hero on the CARD's own width.
    private const double NarrowThreshold = 790;

    public DashboardPageView()
    {
        InitializeComponent();
        // No scan is triggered here: the used/free bar reads DriveInfo live, and the shell runs
        // the full scan once on launch (MainWindow.Opened).
        DiskHero.SizeChanged += OnDiskHeroSizeChanged;
    }

    private void OnDiskHeroSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        bool isNarrow = e.NewSize.Width < NarrowThreshold;
        if (isNarrow == DiskHero.Classes.Contains("narrow"))
            return; // only mutate on a real transition — avoids per-frame class churn
        if (isNarrow)
            DiskHero.Classes.Add("narrow");
        else
            DiskHero.Classes.Remove("narrow");
    }
}
