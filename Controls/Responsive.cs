using Avalonia;
using Avalonia.Controls;

namespace WinButler.Controls;

/// <summary>
/// Attached responsive behavior. Watches a control's width and, once it drops below
/// <c>NarrowUnder</c>, adds a <c>narrow</c> style class to the control and sets the bindable
/// <c>IsNarrow</c> flag. Screens restack via style selectors (e.g.
/// <c>Selector="UserControl.narrow StackPanel.actions"</c>) and templates bind column
/// visibility/width to <c>IsNarrow</c> — all without per-page code-behind.
///
/// Avalonia has no container queries; this is the minimal stand-in, generalized from the
/// one-off <see cref="Views.DashboardPageView"/> SizeChanged handler.
/// </summary>
public static class Responsive
{
    /// <summary>Width (px) below which the host counts as "narrow". <see cref="double.NaN"/> disables.</summary>
    public static readonly AttachedProperty<double> NarrowUnderProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("NarrowUnder", typeof(Responsive), double.NaN);

    /// <summary>True while the host is narrower than <see cref="NarrowUnderProperty"/>. Bindable, read-only in practice.</summary>
    public static readonly AttachedProperty<bool> IsNarrowProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsNarrow", typeof(Responsive), false);

    public static void SetNarrowUnder(Control c, double value) => c.SetValue(NarrowUnderProperty, value);
    public static double GetNarrowUnder(Control c) => c.GetValue(NarrowUnderProperty);
    public static bool GetIsNarrow(Control c) => c.GetValue(IsNarrowProperty);

    static Responsive()
    {
        NarrowUnderProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            c.SizeChanged -= OnSizeChanged;
            c.SizeChanged += OnSizeChanged;
            Apply(c, c.Bounds.Width);
        });
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Apply((Control)sender!, e.NewSize.Width);

    private static void Apply(Control c, double width)
    {
        double threshold = GetNarrowUnder(c);
        if (double.IsNaN(threshold) || width <= 0)
            return;
        bool narrow = width < threshold;
        if (narrow == GetIsNarrow(c))
            return; // only mutate on a real transition — avoids per-frame churn
        c.SetValue(IsNarrowProperty, narrow);
        if (narrow) c.Classes.Add("narrow");
        else c.Classes.Remove("narrow");
    }
}
