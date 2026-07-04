using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WinButler.Converters;

/// <summary>Negates a bool. Used to drive "show when NOT narrow" bindings off the
/// <c>Responsive.IsNarrow</c> attached flag (Avalonia's <c>!</c> binding operator is unreliable
/// against an attached-property-on-ancestor path, so this is the explicit form).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
