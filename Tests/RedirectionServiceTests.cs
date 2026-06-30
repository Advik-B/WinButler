using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
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
