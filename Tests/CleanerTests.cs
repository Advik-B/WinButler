using System;
using System.IO;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public sealed class CleanerTests : IDisposable
{
    private readonly Cleaner _cleaner = new();
    private readonly string _root;

    public CleanerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerClean_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Dry_run_never_deletes()
    {
        var dir = Path.Combine(_root, "safe");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "data");

        var result = await _cleaner.CleanAsync(Target(dir, RiskLevel.Safe), dryRun: true);

        Assert.True(result.Succeeded);
        Assert.True(result.WasDryRun);
        Assert.True(Directory.Exists(dir));   // untouched
    }

    [Fact]
    public async Task Permanent_delete_removes_a_safe_target()
    {
        var dir = Path.Combine(_root, "toremove");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "f.txt"), "data");

        var result = await _cleaner.CleanAsync(Target(dir, RiskLevel.Safe), dryRun: false);

        Assert.True(result.Succeeded);
        Assert.False(result.WasDryRun);
        Assert.False(Directory.Exists(dir));
    }

    [Theory]
    [InlineData(RiskLevel.Safe, DeleteMode.Permanent)]
    [InlineData(RiskLevel.Caution, DeleteMode.RecycleBin)]
    [InlineData(RiskLevel.Risky, DeleteMode.RecycleBin)]
    public void Delete_mode_follows_risk(RiskLevel risk, DeleteMode expected)
    {
        Assert.Equal(expected, Target("C:\\x", risk).DeleteMode);
    }

    private static CleanupTarget Target(string path, RiskLevel risk) => new()
    {
        FullPath = path,
        DisplayName = "t",
        Category = CleanupCategory.Cache,
        Risk = risk,
        Reason = "test",
        SizeBytes = 4,
    };

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
