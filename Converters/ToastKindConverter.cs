using System;
using System.Globalization;
using Avalonia.Data.Converters;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.Converters;

/// <summary>Maps a <see cref="ToastKind"/> to its dot/border brush. "Dry" uses the live app
/// accent since it should re-color on an accent swap same as everything else; the others are
/// fixed signal colors. All resolve from the theme tokens so they can never drift from the XAML.</summary>
public sealed class ToastKindConverter : IValueConverter
{
    public static readonly ToastKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ToastKind.Dry => ThemeService.Brush("WbAccentBrush", "#E60012"),
            ToastKind.Warn => ThemeService.Brush("WbSignalWarnBrush", "#FFB200"),
            ToastKind.Live => ThemeService.Brush("WbSignalLiveBrush", "#E60012"),
            _ => ThemeService.Brush("WbSignalOkBrush", "#06C24A"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
