using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WinButler.Models;

namespace WinButler.Converters;

/// <summary>Maps a <see cref="ToastKind"/> to its dot/border brush. "Dry" uses the live app
/// accent (DynamicResource lookup via <see cref="Application.Current"/>) since it should
/// re-color on an accent swap same as everything else; the others are fixed signal colors.</summary>
public sealed class ToastKindConverter : IValueConverter
{
    public static readonly ToastKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ToastKind.Dry && Application.Current is { } app
            && app.TryGetResource("WbAccentBrush", null, out var accent) && accent is IBrush accentBrush)
        {
            return accentBrush;
        }

        return value switch
        {
            ToastKind.Ok => new SolidColorBrush(Color.Parse("#06C24A")),
            ToastKind.Warn => new SolidColorBrush(Color.Parse("#FFB200")),
            ToastKind.Live => new SolidColorBrush(Color.Parse("#E60012")),
            _ => new SolidColorBrush(Color.Parse("#06C24A")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
