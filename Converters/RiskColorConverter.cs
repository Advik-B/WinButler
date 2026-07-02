using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.Converters;

/// <summary>Maps a <see cref="RiskLevel"/> to its Duly Doted signal-dot brush
/// (SAFE=ok green, CAUTION=warn amber, RISKY=live red — independent of the app accent).
/// Brushes resolve from the theme tokens so they can never drift from the XAML.</summary>
public sealed class RiskColorConverter : IValueConverter
{
    public static readonly RiskColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            RiskLevel.Safe => ThemeService.Brush("WbSignalOkBrush", "#06C24A"),
            RiskLevel.Caution => ThemeService.Brush("WbSignalWarnBrush", "#FFB200"),
            RiskLevel.Risky => ThemeService.Brush("WbSignalLiveBrush", "#E60012"),
            _ => Brushes.Gray,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a <see cref="RiskLevel"/> to the uppercase badge label the mockup uses
/// (SAFE / REVIEW / LOCKED), the counterpart to <see cref="RiskColorConverter"/>.</summary>
public sealed class RiskLabelConverter : IValueConverter
{
    public static readonly RiskLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            RiskLevel.Safe => "SAFE",
            RiskLevel.Caution => "REVIEW",
            RiskLevel.Risky => "LOCKED",
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
