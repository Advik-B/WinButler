using System;
using System.IO;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public sealed class LogTests : IDisposable
{
    private readonly string _dir;

    public LogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WinButlerLog_" + Guid.NewGuid().ToString("N"));
        Log.DirectoryOverride = _dir;
        Log.ResetForTests();
    }

    [Fact]
    public void Writes_a_line_with_level_context_and_message()
    {
        Log.Info("clean", "deleted C:\\x");

        var text = File.ReadAllText(Path.Combine(_dir, "winbutler.log"));
        Assert.Contains("[INFO ]", text);
        Assert.Contains("clean: deleted C:\\x", text);
    }

    [Fact]
    public void Error_includes_the_exception_details()
    {
        Log.Error("redirect", "copy failed", new InvalidOperationException("robocopy exit 16"));

        var text = File.ReadAllText(Path.Combine(_dir, "winbutler.log"));
        Assert.Contains("[ERROR]", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains("robocopy exit 16", text);
    }

    [Fact]
    public void Rotates_an_oversized_file_once_per_session()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "winbutler.log");
        File.WriteAllBytes(file, new byte[3 * 1024 * 1024]);
        Log.ResetForTests();

        Log.Info("test", "after rotation");

        Assert.True(File.Exists(Path.Combine(_dir, "winbutler.prev.log")));
        // Far below the 3 MB pre-rotation size (other tests may append the odd line concurrently).
        Assert.True(new FileInfo(file).Length < 100 * 1024);
        Assert.Contains("after rotation", File.ReadAllText(file));
    }

    [Fact]
    public void Never_throws_when_the_log_location_is_unwritable()
    {
        // Point the "directory" underneath a plain file so CreateDirectory must fail.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        Log.DirectoryOverride = Path.Combine(blocker, "logs");

        Log.Error("test", "must not throw", new Exception("boom"));
    }

    public void Dispose()
    {
        Log.DirectoryOverride = null;
        Log.ResetForTests();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { }
    }
}
