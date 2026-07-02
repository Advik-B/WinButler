using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Services.Mft;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Single-flight / invalidation semantics of the shared disk index, driven through a scan
/// service whose builds complete only when the test says so — no disk, no admin.
/// </summary>
public sealed class DiskIndexServiceTests
{
    /// <summary>A <see cref="DiskScanService"/> whose scans are completed manually by the test.</summary>
    private sealed class ControlledScanService : DiskScanService
    {
        private readonly Queue<TaskCompletionSource<DiskNode>> _pending = new();
        private int _scanCalls;

        public int ScanCalls => Volatile.Read(ref _scanCalls);

        /// <summary>When false, a build ignores its cancellation token and completes anyway —
        /// for exercising the generation guard against stale results.</summary>
        public bool HonorCancellation { get; set; } = true;

        public TaskCompletionSource<DiskNode> ExpectScan()
        {
            var tcs = new TaskCompletionSource<DiskNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending)
                _pending.Enqueue(tcs);
            return tcs;
        }

        public override Task<DiskNode> ScanAsync(string target, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _scanCalls);
            TaskCompletionSource<DiskNode>? tcs;
            lock (_pending)
                tcs = _pending.Count > 0 ? _pending.Dequeue() : null;
            if (tcs is null)
                return Task.FromResult(Node());
            if (HonorCancellation)
                ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }

    private static DiskNode Node() => new() { Name = @"C:\", FullPath = @"C:\", IsDirectory = true };

    [Fact]
    public async Task Concurrent_callers_share_one_build_and_the_result_is_cached()
    {
        var scan = new ControlledScanService();
        var pending = scan.ExpectScan();
        var svc = new DiskIndexService(scan);

        var t1 = svc.EnsureBuiltAsync('C');
        var t2 = svc.EnsureBuiltAsync('C');
        Assert.Equal(1, scan.ScanCalls);          // single-flight: one scan for both callers

        pending.SetResult(Node());
        var i1 = await t1;
        var i2 = await t2;
        Assert.Same(i1.Root, i2.Root);

        await svc.EnsureBuiltAsync('C');          // now cached — still no second scan
        Assert.Equal(1, scan.ScanCalls);
    }

    [Fact]
    public async Task Invalidate_cancels_the_inflight_build_so_no_second_concurrent_read_happens()
    {
        var scan = new ControlledScanService();
        scan.ExpectScan(); // the doomed first build — never completed by the test
        var svc = new DiskIndexService(scan);

        var doomed = svc.EnsureBuiltAsync('C');
        svc.Invalidate('C');                       // must actively cancel, not just forget

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doomed);

        var fresh = scan.ExpectScan();
        var rebuilt = svc.EnsureBuiltAsync('C');
        Assert.Equal(2, scan.ScanCalls);
        fresh.SetResult(Node());
        Assert.Same((await rebuilt), svc.TryGet('C'));
    }

    [Fact]
    public async Task Stale_build_that_ignores_cancellation_is_never_published()
    {
        var scan = new ControlledScanService { HonorCancellation = false };
        var stale = scan.ExpectScan();
        var svc = new DiskIndexService(scan);

        var t1 = svc.EnsureBuiltAsync('C');
        svc.Invalidate('C');
        stale.SetResult(Node());                   // completes anyway — after invalidation

        await t1;                                   // the caller still gets a result…
        Assert.Null(svc.TryGet('C'));               // …but it is never published as current
    }

    [Fact]
    public async Task A_joiner_cancelling_its_wait_leaves_the_shared_build_running()
    {
        var scan = new ControlledScanService();
        var pending = scan.ExpectScan();
        var svc = new DiskIndexService(scan);

        var tA = svc.EnsureBuiltAsync('C');
        using var ctsB = new CancellationTokenSource();
        var tB = svc.EnsureBuiltAsync('C', null, ctsB.Token);

        ctsB.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tB);

        pending.SetResult(Node());                 // A's build was never disturbed
        await tA;
        Assert.NotNull(svc.TryGet('C'));
        Assert.Equal(1, scan.ScanCalls);
    }
}
