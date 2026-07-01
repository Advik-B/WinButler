using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace WinButler.Views.Shared;

/// <summary>A dashboard category card (icon, size readout, count, relative progress bar).
/// Built ahead of the Dashboard screen itself as part of the shared-components pass;
/// the Dashboard task wires real data into it.</summary>
public partial class CategoryCardView : UserControl
{
    public static readonly StyledProperty<string?> IconGlyphProperty =
        AvaloniaProperty.Register<CategoryCardView, string?>(nameof(IconGlyph));
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CategoryCardView, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SizeTextProperty =
        AvaloniaProperty.Register<CategoryCardView, string?>(nameof(SizeText));
    public static readonly StyledProperty<string?> CountTextProperty =
        AvaloniaProperty.Register<CategoryCardView, string?>(nameof(CountText));
    public static readonly StyledProperty<double> BarPercentProperty =
        AvaloniaProperty.Register<CategoryCardView, double>(nameof(BarPercent));
    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<CategoryCardView, ICommand?>(nameof(ClickCommand));

    public string? IconGlyph { get => GetValue(IconGlyphProperty); set => SetValue(IconGlyphProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? SizeText { get => GetValue(SizeTextProperty); set => SetValue(SizeTextProperty, value); }
    public string? CountText { get => GetValue(CountTextProperty); set => SetValue(CountTextProperty, value); }
    public double BarPercent { get => GetValue(BarPercentProperty); set => SetValue(BarPercentProperty, value); }
    public ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    public CategoryCardView()
    {
        InitializeComponent();
    }
}
