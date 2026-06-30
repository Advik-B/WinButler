using System;

namespace WinButler.Services;

/// <summary>Formats byte counts as human-readable sizes (e.g. "1.5 GB").</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{size:0.##} {Units[unit]}";
    }
}
