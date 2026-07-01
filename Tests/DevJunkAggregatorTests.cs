using System;
using System.IO;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public sealed class DevJunkAggregatorTests : IDisposable
{
    private readonly string _root;
    private readonly DevJunkAggregator _agg = new(SafeCaches.FromBundled());

    public DevJunkAggregatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerDevJunk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string MakeToolRoot(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Only_dev_categories_are_included()
    {
        var devRoot = MakeToolRoot("dev-tool");
        var gamesRoot = MakeToolRoot("games-tool");

        var groups = await _agg.BuildAsync(new[]
        {
            Candidate(devRoot, "Build tools"),
            Candidate(gamesRoot, "Games"),
        });

        Assert.Single(groups);
        Assert.Equal(devRoot, groups[0].SourcePath);
    }

    [Fact]
    public async Task Reclaimable_subfolders_are_summed_via_SafeCaches()
    {
        var toolRoot = MakeToolRoot("jetbrains-like");
        // "GPUCache" is an always-safe name regardless of path context (see definitions.json).
        var safeSub = Directory.CreateDirectory(Path.Combine(toolRoot, "GPUCache"));
        File.WriteAllBytes(Path.Combine(safeSub.FullName, "data.bin"), new byte[1024]);
        // An unrecognised folder name should NOT be counted as reclaimable.
        var unknownSub = Directory.CreateDirectory(Path.Combine(toolRoot, "SomeUnknownFolder"));
        File.WriteAllBytes(Path.Combine(unknownSub.FullName, "data.bin"), new byte[2048]);

        var groups = await _agg.BuildAsync(new[] { Candidate(toolRoot, "IDEs") });

        var group = Assert.Single(groups);
        Assert.Equal(1024, group.ReclaimableBytes);
        var target = Assert.Single(group.ReclaimableTargets);
        Assert.Equal(safeSub.FullName, target.FullPath);
        Assert.Equal(RiskLevel.Safe, target.Risk);
    }

    [Fact]
    public async Task Root_containing_a_git_folder_is_locked()
    {
        var toolRoot = MakeToolRoot("dotfiles-like");
        Directory.CreateDirectory(Path.Combine(toolRoot, ".git"));

        var groups = await _agg.BuildAsync(new[] { Candidate(toolRoot, "Misc dev") });

        Assert.True(Assert.Single(groups).IsLocked);
    }

    [Fact]
    public async Task Root_without_a_git_folder_is_not_locked()
    {
        var toolRoot = MakeToolRoot("plain-tool");

        var groups = await _agg.BuildAsync(new[] { Candidate(toolRoot, "Node.js") });

        Assert.False(Assert.Single(groups).IsLocked);
    }

    [Fact]
    public async Task Already_redirected_roots_report_no_reclaimable_targets()
    {
        var toolRoot = MakeToolRoot("already-redirected-tool");
        Directory.CreateDirectory(Path.Combine(toolRoot, "GPUCache"));

        var groups = await _agg.BuildAsync(new[] { Candidate(toolRoot, "Python", isAlreadyRedirected: true) });

        var group = Assert.Single(groups);
        Assert.Empty(group.ReclaimableTargets);
        Assert.Equal(0, group.ReclaimableBytes);
        Assert.True(group.IsAlreadyRedirected);
    }

    private static RedirectCandidate Candidate(string path, string category, bool isAlreadyRedirected = false) => new()
    {
        SourcePath = path,
        DisplayName = Path.GetFileName(path),
        Description = "test tool",
        Category = category,
        TargetName = Path.GetFileName(path),
        SizeBytes = 4096,
        IsAlreadyRedirected = isAlreadyRedirected,
    };

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
