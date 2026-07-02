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
