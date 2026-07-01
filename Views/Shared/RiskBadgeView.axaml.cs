using Avalonia;
using Avalonia.Controls;
using WinButler.Models;

namespace WinButler.Views.Shared;

/// <summary>A colored dot + uppercase label ("SAFE"/"REVIEW"/"LOCKED") for a <see cref="RiskLevel"/>.
/// Shared by the checklist screens (Temp/Cache/Redirect/Dev Junk) so the badge only needs
/// building once — see the shared-components pass.</summary>
public partial class RiskBadgeView : UserControl
{
    public static readonly StyledProperty<RiskLevel> RiskProperty =
        AvaloniaProperty.Register<RiskBadgeView, RiskLevel>(nameof(Risk));

    public RiskLevel Risk
    {
        get => GetValue(RiskProperty);
        set => SetValue(RiskProperty, value);
    }

    public RiskBadgeView()
    {
        InitializeComponent();
    }
}
