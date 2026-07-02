using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// A read-only scanner stub. Tests generally construct pages with these but populate categories
/// directly via <c>CategoryViewModel.SetItems</c> — they avoid <c>ScanCommand</c>, which forces a
/// real MFT read through the concrete <c>DiskIndexService</c> and needs admin/disk.
/// </summary>
public sealed class FakeScanner : IScanner
{
    private readonly IReadOnlyList<CleanupTarget> _results;

    public FakeScanner(CleanupCategory category, string title, IReadOnlyList<CleanupTarget>? results = null)
    {
        Category = category;
        Title = title;
        _results = results ?? Array.Empty<CleanupTarget>();
    }

    public CleanupCategory Category { get; }
    public string Title { get; }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default) =>
        Task.FromResult(_results);
}

/// <summary>A scanner that always faults — exercises the ViewModels' guarded error paths.</summary>
public sealed class ThrowingScanner : IScanner
{
    private readonly Exception _exception;

    public ThrowingScanner(CleanupCategory category, string title, Exception? exception = null)
    {
        Category = category;
        Title = title;
        _exception = exception ?? new System.IO.IOException("scanner exploded");
    }

    public CleanupCategory Category { get; }
    public string Title { get; }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default) =>
        Task.FromException<IReadOnlyList<CleanupTarget>>(_exception);
}

/// <summary>A scanner that never completes until its token is cancelled — for driving the
/// cancel-mid-scan flow deterministically.</summary>
public sealed class HangingScanner : IScanner
{
    public HangingScanner(CleanupCategory category, string title)
    {
        Category = category;
        Title = title;
    }

    public CleanupCategory Category { get; }
    public string Title { get; }

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CleanupTarget>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }
}

/// <summary>
/// A cleaner that always faults. The real <see cref="Cleaner"/> never throws (it reports
/// <c>Succeeded=false</c>), so this exercises the VM-level guard against a misbehaving one.
/// </summary>
public sealed class ThrowingCleaner : ICleaner
{
    public Task<CleanResult> CleanAsync(CleanupTarget target, bool dryRun, CancellationToken ct = default) =>
        Task.FromException<CleanResult>(new System.IO.IOException("cleaner exploded"));
}

/// <summary>
/// A <see cref="Services.Mft.DiskScanService"/> stub returning a canned tree (or faulting)
/// instantly, so headless tests can drive commands that build the shared disk index without a
/// real MFT read or admin rights.
/// </summary>
public sealed class FakeDiskScanService : Services.Mft.DiskScanService
{
    private readonly Func<Services.Mft.DiskNode> _result;

    public FakeDiskScanService(Func<Services.Mft.DiskNode>? result = null) =>
        _result = result ?? (() => new Services.Mft.DiskNode
        {
            Name = @"C:\",
            FullPath = @"C:\",
            IsDirectory = true,
        });

    public override Task<Services.Mft.DiskNode> ScanAsync(
        string target, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try { return Task.FromResult(_result()); }
        catch (Exception ex) { return Task.FromException<Services.Mft.DiskNode>(ex); }
    }
}

/// <summary>A redirection-service stub: no drives, no candidates, no ledger, mutates nothing.</summary>
public sealed class FakeRedirectionService : IRedirectionService
{
    public Task<IReadOnlyList<RedirectCandidate>> ScanCandidatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RedirectCandidate>>(Array.Empty<RedirectCandidate>());

    public Task<RedirectResult> RedirectAsync(RedirectCandidate candidate, string driveLetter, bool dryRun,
        CancellationToken ct = default) =>
        Task.FromResult(new RedirectResult { Succeeded = true, WasDryRun = dryRun, Message = "fake" });

    public Task<RedirectResult> UndoAsync(RedirectRecord record, bool dryRun, CancellationToken ct = default) =>
        Task.FromResult(new RedirectResult { Succeeded = true, WasDryRun = dryRun, Message = "fake" });

    public IReadOnlyList<RedirectRecord> GetActiveRedirects() => Array.Empty<RedirectRecord>();

    public IReadOnlyList<string> FindOrphanedRedirects() => Array.Empty<string>();

    public IReadOnlyList<string> GetEligibleDrives() => Array.Empty<string>();

    public string? SuggestTargetDrive() => null;
}

/// <summary>
/// A cleaner stub that mutates nothing and always reports success, projecting the target's own size
/// as reclaimed. Lets the dry-run clean path be exercised with no filesystem access.
/// </summary>
public sealed class FakeCleaner : ICleaner
{
    public List<(CleanupTarget Target, bool DryRun)> Calls { get; } = new();

    public Task<CleanResult> CleanAsync(CleanupTarget target, bool dryRun, CancellationToken ct = default)
    {
        Calls.Add((target, dryRun));
        return Task.FromResult(new CleanResult
        {
            Target = target,
            Succeeded = true,
            WasDryRun = dryRun,
            BytesReclaimed = target.SizeBytes,
        });
    }
}

/// <summary>Terse builders for the plain data types the tests feed into the ViewModels.</summary>
public static class Fakes
{
    public static CleanupTarget Target(
        string name, long sizeBytes, RiskLevel risk = RiskLevel.Safe,
        CleanupCategory category = CleanupCategory.Cache) => new()
    {
        FullPath = $@"C:\fake\{name}",
        DisplayName = name,
        Category = category,
        SizeBytes = sizeBytes,
        Risk = risk,
        Reason = "test",
    };

    /// <summary>The three Clean scanners in the order the real shell wires them, so the page's
    /// Electron/Temp/Cache category lookups resolve.</summary>
    public static IReadOnlyList<IScanner> CleanScanners() => new IScanner[]
    {
        new FakeScanner(CleanupCategory.ElectronLeftover, "Electron Leftovers"),
        new FakeScanner(CleanupCategory.Temp, "Temp Files"),
        new FakeScanner(CleanupCategory.Cache, "Cache Sweep"),
    };
}

/// <summary>
/// Base for headless tests: resets the process-wide <see cref="WeakReferenceMessenger.Default"/>
/// before and after each test. Live ViewModels (notably <c>DashboardPageViewModel</c>) register on
/// that static singleton in their constructors, so without this a message sent by one test can fire
/// a handler still registered by another — the classic passes-locally-flakes-in-CI trap.
///
/// The per-test reset is only sound if these tests don't run concurrently, so every headless test
/// class carries <c>[Collection(HeadlessCollection.Name)]</c>: xunit never parallelizes tests in the
/// same collection (independent of how the Avalonia headless framework schedules the UI thread).
/// </summary>
public abstract class MessengerIsolatedTest : IDisposable
{
    protected MessengerIsolatedTest() => WeakReferenceMessenger.Default.Reset();
    public void Dispose() => WeakReferenceMessenger.Default.Reset();
}

/// <summary>Names the single xunit collection that serializes all headless tests (see
/// <see cref="MessengerIsolatedTest"/> for why they must not run in parallel).</summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection
{
    public const string Name = "Headless UI";
}
