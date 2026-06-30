using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WinButler.Models;

namespace WinButler.Converters;

/// <summary>Maps a <see cref="RiskLevel"/> to a badge brush for the dashboard.</summary>
public sealed class RiskColorConverter : IValueConverter
{
    public static readonly RiskColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            RiskLevel.Safe => new SolidColorBrush(Color.Parse("#2E7D32")),    // green
            RiskLevel.Caution => new SolidColorBrush(Color.Parse("#ED6C02")), // amber
            RiskLevel.Risky => new SolidColorBrush(Color.Parse("#C62828")),   // red
            _ => Brushes.Gray,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
