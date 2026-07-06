using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Covers <see cref="KnownLocationsScanner"/>'s three modes, risk mapping, deny-list gating and
/// token expansion. Entries point at absolute fixture paths via the internal <c>ScanEntry</c> seam.
/// </summary>
public sealed class KnownLocationsScannerTests : IDisposable
{
    private readonly string _root;
    private readonly KnownLocationsScanner _scanner = new(SafeCaches.FromBundled(), new KnownLocationRuleSet());

    public KnownLocationsScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerKnown_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private List<CleanupTarget> Scan(KnownLocationEntry entry)
    {
        var results = new List<CleanupTarget>();
        _scanner.ScanEntry(entry, results, default);
        return results;
    }

    private static void WriteFile(string path, string content = "data")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Self_mode_flags_the_directory_itself_as_one_target()
    {
        var dir = Path.Combine(_root, "obs-logs");
        WriteFile(Path.Combine(dir, "session.txt"));

        var results = Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "self", Risk = "safe", DisplayName = "OBS logs",
        });

        var target = Assert.Single(results);
        Assert.Equal(dir, target.FullPath);
        Assert.Equal(RiskLevel.Safe, target.Risk);
        Assert.True(target.SizeBytes > 0);
    }

    [Fact]
    public void Children_mode_flags_each_child_but_not_the_parent()
    {
        var dir = Path.Combine(_root, "crashes");
        WriteFile(Path.Combine(dir, "a", "dump1.bin"));
        WriteFile(Path.Combine(dir, "b", "dump2.bin"));
        WriteFile(Path.Combine(dir, "loose.bin"));

        var results = Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "children", Risk = "safe", DisplayName = "Crashes",
        });

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, t => t.FullPath.Equals(dir, StringComparison.OrdinalIgnoreCase));
        Assert.All(results, t => Assert.StartsWith(dir, t.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Files_mode_matches_pattern_recursively_and_ignores_others()
    {
        var dir = Path.Combine(_root, "profile");
        WriteFile(Path.Combine(dir, "top.dmp"));
        WriteFile(Path.Combine(dir, "sub", "nested.dmp"));
        WriteFile(Path.Combine(dir, "sub", "keep.txt"));

        var results = Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "files", Pattern = "*.dmp", Recursive = true, Risk = "safe",
            DisplayName = "Dumps",
        });

        Assert.Equal(2, results.Count);
        Assert.All(results, t => Assert.EndsWith(".dmp", t.FullPath));
    }

    [Fact]
    public void Files_mode_without_recursive_stays_shallow()
    {
        var dir = Path.Combine(_root, "shallow");
        WriteFile(Path.Combine(dir, "top.log"));
        WriteFile(Path.Combine(dir, "sub", "deep.log"));

        var results = Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "files", Pattern = "*.log", Recursive = false, Risk = "safe",
            DisplayName = "Logs",
        });

        var target = Assert.Single(results);
        Assert.EndsWith("top.log", target.FullPath);
    }

    [Fact]
    public void Deny_listed_children_are_never_offered()
    {
        var dir = Path.Combine(_root, "mixed");
        WriteFile(Path.Combine(dir, "junk", "x.bin"));
        WriteFile(Path.Combine(dir, ".ssh", "id_ed25519"), "key material"); // deny fragment "\.ssh"

        var results = Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "children", Risk = "safe", DisplayName = "Mixed",
        });

        var target = Assert.Single(results);
        Assert.EndsWith("junk", target.FullPath);
        Assert.DoesNotContain(results, t => t.FullPath.Contains(".ssh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Risky_entries_map_to_the_risky_level_and_the_recycle_bin()
    {
        var dir = Path.Combine(_root, "history");
        WriteFile(Path.Combine(dir, "edit.json"));

        var target = Assert.Single(Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "self", Risk = "risky", DisplayName = "VS Code history",
        }));

        Assert.Equal(RiskLevel.Risky, target.Risk);
        Assert.Equal(DeleteMode.RecycleBin, target.DeleteMode); // never permanently deleted
    }

    [Fact]
    public void Empty_targets_are_skipped()
    {
        var dir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(dir);

        Assert.Empty(Scan(new KnownLocationEntry
        {
            Id = "t", Path = dir, Mode = "self", Risk = "safe", DisplayName = "Empty",
        }));
    }

    [Fact]
    public void Missing_paths_yield_nothing()
    {
        Assert.Empty(Scan(new KnownLocationEntry
        {
            Id = "t", Path = Path.Combine(_root, "does-not-exist"), Mode = "self", Risk = "safe",
            DisplayName = "Ghost",
        }));
    }

    [Fact]
    public void ExpandTokens_resolves_custom_and_environment_tokens()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(local, "Foo"), KnownLocationsScanner.ExpandTokens("%LocalAppData%\\Foo"));
        Assert.Equal(Path.Combine(docs, "Bar"), KnownLocationsScanner.ExpandTokens("%Documents%\\Bar"));
        Assert.Equal(Path.Combine(profile, "AppData", "LocalLow", "Baz"),
            KnownLocationsScanner.ExpandTokens("%LocalLow%\\Baz"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
