using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Principal;
using WinButler.Services.Mft;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Verifies the WizTree-style MFT engine. The pure binary helpers (USA fix-up, data-run
/// decoder) and the recursive fallback are deterministic and always run; the full raw-volume
/// scan only runs elevated on an NTFS C: (the user's normal setup), where it cross-checks the
/// scanned total against the volume's actual used space.
/// </summary>
public sealed class MftReaderTests
{
    private readonly ITestOutputHelper _out;

    public MftReaderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Usa_fixup_restores_sector_tail_bytes()
    {
        // A 1024-byte record spanning two 512-byte sectors: usaCount = 1 USN + 2 fix-up words.
        var rec = new byte[1024];
        rec[0] = (byte)'F'; rec[1] = (byte)'I'; rec[2] = (byte)'L'; rec[3] = (byte)'E';

        const int usaOffset = 0x30;
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(4), usaOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(6), 3);

        // USA: [USN, fixup0, fixup1]. The real tail bytes live in the fix-up array.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(usaOffset + 0), 0xAAAA); // USN sentinel
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(usaOffset + 2), 0x1111);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(usaOffset + 4), 0x2222);

        // On disk, each sector's last word was overwritten with the USN — that's what we restore.
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(510), 0xAAAA);
        BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1022), 0xAAAA);

        MftReader.ApplyUsaFixup(rec);

        Assert.Equal(0x1111, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(510)));
        Assert.Equal(0x2222, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(1022)));
    }

    [Fact]
    public void Data_run_decoder_handles_positive_and_negative_deltas()
    {
        // 21 18 3412  -> len 1B (0x18=24), off 2B (0x1234=+4660)         => (4660, 24)
        // 11 10 E0    -> len 1B (0x10=16), off 1B (0xE0 = -32)           => (4628, 16)
        // 00          -> terminator
        byte[] runs = { 0x21, 0x18, 0x34, 0x12, 0x11, 0x10, 0xE0, 0x00 };

        var extents = MftReader.DecodeDataRuns(runs);

        Assert.Equal(2, extents.Count);
        Assert.Equal((4660L, 24L), extents[0]);
        Assert.Equal((4628L, 16L), extents[1]);
    }

    [Fact]
    public void Recursive_walk_aggregates_sizes_and_counts()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "WB_walk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllBytes(Path.Combine(root, "f1.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "a", "f2.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(root, "a", "b", "f3.bin"), new byte[3000]);

        try
        {
            var node = new RecursiveWalkScanner().Scan(root);

            Assert.Equal(6000, node.SizeBytes);
            Assert.Equal(3, node.FileCount);
            Assert.Equal(2, node.FolderCount);          // "a" and "a\b"
            Assert.Equal("a", node.Children[0].Name);   // 5000-byte subtree sorts above the 1000-byte file
            Assert.InRange(node.Children[0].PercentOfParent, 0.83, 0.84); // 5000/6000
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ground truth for the parser, cross-checked against an ordinary recursive walk of the same
    /// folder (which itself matches PowerShell's <c>Measure-Object Length</c> byte-for-byte). The
    /// exact invariants: the MFT engine never invents a path; every file both engines see is sized
    /// identically; and the engines' totals differ ONLY by hardlinked duplicate names, which the
    /// MFT counts once per record. (That last bit is why a whole-drive MFT total reads a little
    /// lower than the walk — by design, and documented on <see cref="MftReader"/>.)
    /// </summary>
    [Fact]
    public async Task Mft_subfolder_scan_agrees_with_recursive_walk()
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!IsElevatedNtfsC())
            return;

        const string target = @"C:\Program Files (x86)";
        if (!Directory.Exists(target))
            return;

        var mft = await new DiskScanService().ScanAsync(target);
        var walk = new RecursiveWalkScanner().Scan(target);

        var mftMap = FileSizesByPath(mft);
        var walkMap = FileSizesByPath(walk);

        long onlyWalk = walkMap.Where(kv => !mftMap.ContainsKey(kv.Key)).Sum(kv => kv.Value);
        int perPathDisagreements = walkMap.Count(kv => mftMap.TryGetValue(kv.Key, out var v) && v != kv.Value);

        _out.WriteLine($"MFT size={mft.SizeBytes:N0} files={mft.FileCount:N0}; walk size={walk.SizeBytes:N0} files={walk.FileCount:N0}");
        _out.WriteLine($"hardlink-only-in-walk bytes={onlyWalk:N0}; per-path disagreements={perPathDisagreements}");

        // Aggregation is exact (no phantom bytes): root total equals the sum of its leaves.
        Assert.Equal(mft.SizeBytes, Flatten(mft).Where(n => !n.IsDirectory).Sum(n => n.SizeBytes));
        // The MFT never reports a path the authoritative walk didn't see.
        Assert.DoesNotContain(mftMap.Keys, k => !walkMap.ContainsKey(k));
        // Every file seen by both is sized identically — the parser's per-file sizes are correct.
        Assert.Equal(0, perPathDisagreements);
        // The whole difference between the engines is hardlinked duplicate names.
        Assert.Equal(walk.SizeBytes, mft.SizeBytes + onlyWalk);
    }

    private static System.Collections.Generic.Dictionary<string, long> FileSizesByPath(DiskNode root) =>
        Flatten(root).Where(n => !n.IsDirectory)
            .GroupBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(n => n.SizeBytes), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whole-drive smoke test: the scan completes and produces a plausible big tree.</summary>
    [Fact]
    public void Mft_scan_of_C_produces_plausible_tree()
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (!IsElevatedNtfsC())
            return;

        var root = new MftTreeBuilder().Build(new MftReader().Read('C'), 'C');
        long used = new DriveInfo("C:\\").TotalSize - new DriveInfo("C:\\").AvailableFreeSpace;

        _out.WriteLine($"root real={root.SizeBytes:N0} files={root.FileCount:N0} used={used:N0}");

        Assert.True(root.FileCount > 1000, $"Implausibly few files: {root.FileCount}");
        // Logical size should be in the right ballpark of physical usage (hardlinks push it higher).
        Assert.True(root.SizeBytes > used * 0.5, $"Scanned {root.SizeBytes:N0} far below used {used:N0}.");
    }

    private static System.Collections.Generic.IEnumerable<DiskNode> Flatten(DiskNode root)
    {
        var stack = new System.Collections.Generic.Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in n.Children) stack.Push(c);
        }
    }

    private static bool IsElevatedNtfsC()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (!string.Equals(new DriveInfo("C:\\").DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            return false;
        // Opening the raw volume needs elevation; skip cleanly when not elevated.
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
