using Avalonia;
using Avalonia.Controls;

namespace WinButler.Views.Shared;

/// <summary>The pill-shaped dry-run indicator + switch used in both the toolbar and the
/// status bar — see the shared-components pass.</summary>
public partial class DryRunPillView : UserControl
{
    public static readonly StyledProperty<bool> IsDryRunProperty =
        AvaloniaProperty.Register<DryRunPillView, bool>(nameof(IsDryRun), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool IsDryRun
    {
        get => GetValue(IsDryRunProperty);
        set => SetValue(IsDryRunProperty, value);
    }

    public DryRunPillView()
    {
        InitializeComponent();
    }
}
