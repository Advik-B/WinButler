using System;
using System.Collections.Generic;
using System.Linq;
using WinButler.Services;
using WinButler.Services.Privacy;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Covers <see cref="PrivacyCleaner"/>'s registry handling via a fake editor (the real registry is
/// never touched in tests). Dry-run must count without deleting; live must delete only the expected
/// values; absent 7-Zip values must be skipped silently.
/// </summary>
public sealed class PrivacyCleanerTests
{
    private sealed class FakeRegistry : IRegistryEditor
    {
        // subKey -> value names
        public Dictionary<string, List<string>> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string SubKey, string Value)> Deleted { get; } = new();

        public IReadOnlyList<string> GetValueNames(string subKey) =>
            Keys.TryGetValue(subKey, out var v) ? v.ToList() : Array.Empty<string>();

        public void DeleteValue(string subKey, string valueName)
        {
            Deleted.Add((subKey, valueName));
            if (Keys.TryGetValue(subKey, out var v))
                v.RemoveAll(n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class NullProgress : IProgress<string> { public void Report(string value) { } }

    private const string RunMru = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private const string SevenZipFm = @"Software\7-Zip\FM";

    [Fact]
    public void Explorer_dry_run_counts_registry_values_without_deleting()
    {
        var reg = new FakeRegistry();
        reg.Keys[RunMru] = new List<string> { "a", "b", "MRUList" };

        var result = new PrivacyCleaner(reg).ClearExplorerHistory(dryRun: true, new NullProgress());

        Assert.Equal(3, result.RegistryValuesRemoved); // counted
        Assert.Empty(reg.Deleted);                     // but nothing actually removed
    }

    [Fact]
    public void Explorer_live_run_deletes_every_mru_value()
    {
        var reg = new FakeRegistry();
        reg.Keys[RunMru] = new List<string> { "a", "b", "MRUList" };

        var result = new PrivacyCleaner(reg).ClearExplorerHistory(dryRun: false, new NullProgress());

        Assert.Equal(3, result.RegistryValuesRemoved);
        Assert.Equal(3, reg.Deleted.Count(d => d.SubKey == RunMru));
    }

    [Fact]
    public void SevenZip_deletes_only_the_known_values_that_exist()
    {
        var reg = new FakeRegistry();
        // Only two of the four known values are present, plus an unrelated one that must be left alone.
        reg.Keys[SevenZipFm] = new List<string> { "FolderHistory", "PanelPath0", "SomethingElse" };

        var result = new PrivacyCleaner(reg).ClearSevenZipHistory(dryRun: false, new NullProgress());

        Assert.Equal(2, result.RegistryValuesRemoved);
        Assert.Contains(reg.Deleted, d => d.Value == "FolderHistory");
        Assert.Contains(reg.Deleted, d => d.Value == "PanelPath0");
        Assert.DoesNotContain(reg.Deleted, d => d.Value == "SomethingElse");
    }

    [Fact]
    public void SevenZip_with_no_key_is_a_silent_no_op()
    {
        var reg = new FakeRegistry(); // 7-Zip never installed → key absent

        var result = new PrivacyCleaner(reg).ClearSevenZipHistory(dryRun: false, new NullProgress());

        Assert.Equal(0, result.RegistryValuesRemoved);
        Assert.Empty(reg.Deleted);
    }
}
