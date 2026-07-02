using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Definitions;
using Xunit;

namespace WinButler.Tests;

public sealed class RedirectionServiceTests : IDisposable
{
    private readonly RedirectionService _svc = new();
    private readonly string _root;

    public RedirectionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerRedir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private static string SystemDriveLetter =>
        Path.GetPathRoot(Environment.SystemDirectory)!.Substring(0, 1);

    [Fact]
    public void Eligible_drives_are_single_letters()
    {
        var drives = _svc.GetEligibleDrives();
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.Equal(1, d.Length));
    }

    [Fact]
    public void Suggested_drive_is_eligible_or_null()
    {
        var suggested = _svc.SuggestTargetDrive();
        if (suggested != null)
            Assert.Contains(suggested, _svc.GetEligibleDrives());
    }

    [Fact]
    public async Task Scan_candidates_all_carry_targetname_and_exist()
    {
        var cands = await _svc.ScanCandidatesAsync();
        Assert.All(cands, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.TargetName));
            Assert.True(Directory.Exists(c.SourcePath));
        });
    }

    [Fact]
    public async Task Dry_run_redirect_touches_nothing()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "f.txt"), "data");

        var targetName = "WinButlerTest_" + Guid.NewGuid().ToString("N");
        var candidate = MakeCandidate(source, targetName);
        var dest = Path.Combine($"{SystemDriveLetter}:\\", "_redirected", targetName);

        var result = await _svc.RedirectAsync(candidate, SystemDriveLetter, dryRun: true);

        Assert.True(result.Succeeded);
        Assert.True(result.WasDryRun);
        Assert.True(Directory.Exists(source));           // source untouched
        Assert.False(Directory.Exists(dest));            // nothing created
    }

    [Fact]
    public async Task Redirect_rejects_non_ntfs_or_missing_drive()
    {
        var source = Path.Combine(_root, "src2");
        Directory.CreateDirectory(source);
        var candidate = MakeCandidate(source, "WinButlerTest_bad");

        // A drive letter that is (almost certainly) not present.
        var result = await _svc.RedirectAsync(candidate, "Q", dryRun: true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Redirect_fails_cleanly_when_dest_already_has_data()
    {
        var source = Path.Combine(_root, "src3");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "f.txt"), "data");

        var targetName = "WinButlerTest_" + Guid.NewGuid().ToString("N");
        var dest = Path.Combine($"{SystemDriveLetter}:\\", "_redirected", targetName);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "existing.txt"), "occupied");

        try
        {
            // The occupied-dest guard must fail the validation (not throw) on both paths,
            // and must mutate neither side.
            foreach (var dryRun in new[] { true, false })
            {
                var result = await _svc.RedirectAsync(MakeCandidate(source, targetName), SystemDriveLetter, dryRun);

                Assert.False(result.Succeeded);
                Assert.Equal(dryRun, result.WasDryRun);
                Assert.Contains("not empty", result.Message);
                Assert.True(File.Exists(Path.Combine(source, "f.txt")));
                Assert.True(File.Exists(Path.Combine(dest, "existing.txt")));
            }
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Corrupt_ledger_is_preserved_as_a_copy_and_service_starts_empty()
    {
        var ledgerPath = Path.Combine(_root, "bad.json");
        File.WriteAllText(ledgerPath, "{ this is not json");

        var svc = new RedirectionService(BundledDefinitionSource.Load().Redirect, ledgerPath);

        Assert.Empty(svc.GetActiveRedirects());
        Assert.True(File.Exists(ledgerPath + ".corrupt"));   // damaged records kept recoverable
        Assert.Equal("{ this is not json", File.ReadAllText(ledgerPath + ".corrupt"));
    }

    [Fact]
    public void Orphan_detection_reports_unledgered_folders_only()
    {
        var redirectRoot = Path.Combine(_root, "_redirected");
        var known = Path.Combine(redirectRoot, "known-tool");
        var orphan = Path.Combine(redirectRoot, "orphan-tool");
        Directory.CreateDirectory(known);
        Directory.CreateDirectory(orphan);

        var ledgerPath = Path.Combine(_root, "orphan-ledger.json");
        File.WriteAllText(ledgerPath, $$"""
            [{ "SourcePath": "C:\\x", "TargetPath": {{System.Text.Json.JsonSerializer.Serialize(known)}},
               "TimestampUtc": "2026-01-01T00:00:00Z", "SizeBytes": 1 }]
            """);
        var svc = new RedirectionService(BundledDefinitionSource.Load().Redirect, ledgerPath);

        var orphans = svc.FindOrphanedRedirects(new[] { redirectRoot });

        Assert.Single(orphans);
        Assert.Equal(orphan, orphans[0]);
    }

    [Fact]
    public async Task Live_redirect_then_undo_round_trips_data_junction_and_ledger()
    {
        var ledgerPath = Path.Combine(_root, "live-ledger.json");
        var svc = new RedirectionService(BundledDefinitionSource.Load().Redirect, ledgerPath);

        var source = Path.Combine(_root, "live-src");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(source, "nested", "b.txt"), "beta");

        var targetName = "WinButlerTest_" + Guid.NewGuid().ToString("N");
        var dest = Path.Combine($"{SystemDriveLetter}:\\", "_redirected", targetName);

        try
        {
            // Redirect for real: copy → verify → delete original → junction → ledger.
            var result = await svc.RedirectAsync(MakeCandidate(source, targetName), SystemDriveLetter, dryRun: false);
            Assert.True(result.Succeeded, result.Message);
            Assert.True(Junction.IsJunction(source));
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(source, "a.txt")));      // via the junction
            Assert.True(File.Exists(Path.Combine(dest, "nested", "b.txt")));
            Assert.Single(svc.GetActiveRedirects());

            // The atomic write left a loadable ledger a fresh instance can read back.
            var reloaded = new RedirectionService(BundledDefinitionSource.Load().Redirect, ledgerPath);
            Assert.Single(reloaded.GetActiveRedirects());
            Assert.False(File.Exists(ledgerPath + ".tmp"));

            // Undo: junction removed, data restored, target copy gone, ledger emptied.
            var undo = await svc.UndoAsync(svc.GetActiveRedirects()[0], dryRun: false);
            Assert.True(undo.Succeeded, undo.Message);
            Assert.False(Junction.IsJunction(source));
            Assert.Equal("beta", File.ReadAllText(Path.Combine(source, "nested", "b.txt")));
            Assert.False(Directory.Exists(dest));
            Assert.Empty(svc.GetActiveRedirects());
        }
        finally
        {
            try { if (Junction.IsJunction(source)) Junction.Remove(source); } catch { }
            try { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Pre_cancelled_redirect_throws_and_mutates_nothing()
    {
        var svc = new RedirectionService(
            BundledDefinitionSource.Load().Redirect, Path.Combine(_root, "cancel-ledger.json"));
        var source = Path.Combine(_root, "cancel-src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "f.txt"), "data");

        var targetName = "WinButlerTest_" + Guid.NewGuid().ToString("N");
        var dest = Path.Combine($"{SystemDriveLetter}:\\", "_redirected", targetName);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.RedirectAsync(MakeCandidate(source, targetName), SystemDriveLetter, dryRun: false, cts.Token));

        Assert.True(File.Exists(Path.Combine(source, "f.txt")));   // source untouched
        Assert.False(Directory.Exists(dest));                       // nothing created
        Assert.Empty(svc.GetActiveRedirects());                     // no ledger entry
    }

    private static RedirectCandidate MakeCandidate(string source, string targetName) => new()
    {
        SourcePath = source,
        DisplayName = "Test",
        Description = "test",
        TargetName = targetName,
        SizeBytes = 4,
    };

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
