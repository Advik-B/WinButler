using System;
using System.IO;

namespace WinButler.Services;

/// <summary>
/// Minimal append-only file logger for diagnosing crash reports and auditing destructive
/// actions. Writes to %APPDATA%\WinButler\logs\winbutler.log, rotating once per session
/// (to winbutler.prev.log) when the file exceeds 2 MB. Logging must never throw — a
/// diagnostic path that can take the app down is worse than no diagnostics at all.
/// </summary>
public static class Log
{
    private const long RotateAtBytes = 2 * 1024 * 1024;

    private static readonly object Sync = new();
    private static bool _rotationChecked;

    /// <summary>Test seam: redirects output; null = the default %APPDATA% location.</summary>
    internal static string? DirectoryOverride { get; set; }

    /// <summary>Test seam: re-arms the once-per-session rotation check.</summary>
    internal static void ResetForTests()
    {
        lock (Sync)
            _rotationChecked = false;
    }

    public static void Info(string context, string message) => Write("INFO", context, message, null);

    public static void Warn(string context, string message, Exception? ex = null) => Write("WARN", context, message, ex);

    public static void Error(string context, string message, Exception? ex = null) => Write("ERROR", context, message, ex);

    private static void Write(string level, string context, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                var dir = DirectoryOverride ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinButler", "logs");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "winbutler.log");

                if (!_rotationChecked)
                {
                    _rotationChecked = true;
                    RotateIfOversized(dir, file);
                }

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {context}: {message}";
                if (ex is not null)
                    line += Environment.NewLine + "    " +
                            ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");

                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow everything: logging is best-effort by design.
        }
    }

    private static void RotateIfOversized(string dir, string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length <= RotateAtBytes)
            return;

        File.Move(file, Path.Combine(dir, "winbutler.prev.log"), overwrite: true);
    }
}
