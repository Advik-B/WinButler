using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WinButler.Models;

namespace WinButler.Converters;

/// <summary>Maps a <see cref="RiskLevel"/> to its Duly Doted signal-dot brush
/// (SAFE=ok green, CAUTION=warn amber, RISKY=live red — independent of the app accent).</summary>
public sealed class RiskColorConverter : IValueConverter
{
    public static readonly RiskColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            RiskLevel.Safe => new SolidColorBrush(Color.Parse("#06C24A")),
            RiskLevel.Caution => new SolidColorBrush(Color.Parse("#FFB200")),
            RiskLevel.Risky => new SolidColorBrush(Color.Parse("#E60012")),
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
