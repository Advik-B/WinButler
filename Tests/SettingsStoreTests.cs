using System;
using System.IO;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WinButlerSettings_" + Guid.NewGuid().ToString("N"));
        SettingsStore.DirectoryOverride = _dir;
    }

    [Fact]
    public void Round_trips_accent_and_target_drive()
    {
        SettingsStore.Save(new AppSettings { Accent = AccentKind.Green, TargetDrive = "D" });

        var loaded = new AppSettings(); // fresh defaults
        SettingsStore.Load(loaded);

        Assert.Equal(AccentKind.Green, loaded.Accent);
        Assert.Equal("D", loaded.TargetDrive);
    }

    [Fact]
    public void Dry_run_is_never_persisted()
    {
        // Even saved from a dry-run-OFF session…
        SettingsStore.Save(new AppSettings { IsDryRun = false, Accent = AccentKind.Green });

        // …the file contains no dry-run key…
        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("DryRun", json, StringComparison.OrdinalIgnoreCase);

        // …and a fresh load starts dry-run ON (the safety default).
        var loaded = new AppSettings();
        SettingsStore.Load(loaded);
        Assert.True(loaded.IsDryRun);
    }

    [Fact]
    public void Missing_file_leaves_defaults_untouched()
    {
        var settings = new AppSettings();
        SettingsStore.Load(settings); // nothing saved yet

        Assert.Equal(AccentKind.Red, settings.Accent);
        Assert.Null(settings.TargetDrive);
        Assert.True(settings.IsDryRun);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_without_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not valid json ");

        var settings = new AppSettings { Accent = AccentKind.Green };
        SettingsStore.Load(settings); // must not throw

        Assert.Equal(AccentKind.Green, settings.Accent); // unchanged from before the load
    }

    public void Dispose()
    {
        SettingsStore.DirectoryOverride = null;
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { }
    }
}
