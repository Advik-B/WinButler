using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using WinButler.Services;
using WinButler.Services.Mft;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Measurement probes for the scanning hot paths — not assertions of speed (timing asserts
/// flake), just Stopwatch + allocation numbers via ITestOutputHelper so perf changes carry
/// honest before/after evidence. The MFT probe needs elevation + NTFS C: (like the reader's
/// integration tests); the walk probe runs anywhere.
/// </summary>
public sealed class PerfProbeTests
{
    private readonly ITestOutputHelper _out;

    public PerfProbeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Probe_mft_read_tree_build_and_index_on_C()
    {
        if (!IsElevatedNtfsC())
            return;

        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();

        var entries = new MftReader().Read('C');
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        long readMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var root = new MftTreeBuilder().Build(entries, 'C');
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        long buildMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var index = DriveIndex.Build('C', root);
        long a3 = GC.GetAllocatedBytesForCurrentThread();
        long indexMs = sw.ElapsedMilliseconds;

        _out.WriteLine($"records={entries.Length:N0} files={root.FileCount:N0} folders={root.FolderCount:N0}");
        _out.WriteLine($"MftReader.Read    {readMs,6:N0} ms   {(a1 - a0) / 1_000_000.0,10:N1} MB alloc");
        _out.WriteLine($"MftTreeBuilder    {buildMs,6:N0} ms   {(a2 - a1) / 1_000_000.0,10:N1} MB alloc");
        _out.WriteLine($"DriveIndex.Build  {indexMs,6:N0} ms   {(a3 - a2) / 1_000_000.0,10:N1} MB alloc");

        Assert.True(root.FileCount > 0);
        Assert.NotNull(index.GetSize(@"C:\Windows"));
    }

    [Fact]
    public void Probe_recursive_walk_and_size_calculator()
    {
        const string target = @"C:\Program Files (x86)";
        if (!OperatingSystem.IsWindows() || !Directory.Exists(target))
            return;

        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();

        var node = new RecursiveWalkScanner().Scan(target);
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        long walkMs = sw.ElapsedMilliseconds;

        sw.Restart();
        long size = DirectorySizeCalculator.GetSize(target);
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        long sizeMs = sw.ElapsedMilliseconds;

        _out.WriteLine($"target={target} files={node.FileCount:N0} bytes={node.SizeBytes:N0}");
        _out.WriteLine($"RecursiveWalkScanner       {walkMs,6:N0} ms   {(a1 - a0) / 1_000_000.0,10:N1} MB alloc");
        _out.WriteLine($"DirectorySizeCalculator    {sizeMs,6:N0} ms   {(a2 - a1) / 1_000_000.0,10:N1} MB alloc");

        Assert.True(node.SizeBytes > 0);
        Assert.True(size > 0);
    }

    private static bool IsElevatedNtfsC()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (!string.Equals(new DriveInfo("C:\\").DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            return false;
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
