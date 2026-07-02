using System.Collections.Generic;
using WinButler.Services.Mft;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Tree-builder tests against hand-built entry arrays — no disk. The core invariant across all
/// corruption shapes (cycles, orphans, self-parents): the build terminates promptly and every
/// in-use record's bytes end up under the root exactly once (byte conservation).
/// </summary>
public sealed class MftTreeBuilderTests
{
    private const int Root = 5; // NTFS reserves record 5 for the volume root.

    private static MftEntry Dir(uint no, uint parent, string name) =>
        new(no, parent, name, IsDirectory: true, RealSize: 0, AllocSize: 0, ModifiedTicks: 0, InUse: true);

    private static MftEntry File(uint no, uint parent, string name, long size) =>
        new(no, parent, name, IsDirectory: false, RealSize: size, AllocSize: size, ModifiedTicks: 0, InUse: true);

    private static MftEntry[] NewEntries(int count)
    {
        var e = new MftEntry[count];
        e[Root] = Dir(Root, Root, "");
        return e;
    }

    private static long SumOfFiles(DiskNode root)
    {
        long sum = 0;
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsDirectory)
                sum += n.SizeBytes;
            foreach (var c in n.Children)
                stack.Push(c);
        }
        return sum;
    }

    [Fact]
    public void Normal_tree_aggregates_sizes_counts_and_paths()
    {
        var e = NewEntries(10);
        e[6] = Dir(6, Root, "dir");
        e[7] = File(7, 6, "f1.bin", 100);
        e[8] = File(8, 6, "f2.bin", 200);

        var root = new MftTreeBuilder().Build(e, 'C');

        Assert.Equal(300, root.SizeBytes);
        Assert.Equal(2, root.FileCount);
        Assert.Equal(1, root.FolderCount);
        var dir = Assert.Single(root.Children);
        Assert.Equal(300, dir.SizeBytes);
        Assert.Equal(@"C:\dir", dir.FullPath);
        Assert.Equal(@"C:\dir\f2.bin", dir.Children[0].FullPath); // largest-first
    }

    [Fact]
    public void Two_node_parent_cycle_is_broken_and_bytes_are_conserved()
    {
        var e = NewEntries(10);
        e[6] = Dir(6, 7, "a");      // a's parent is b…
        e[7] = Dir(7, 6, "b");      // …and b's parent is a — both individually valid.
        e[8] = File(8, 6, "f.bin", 100);

        var root = new MftTreeBuilder().Build(e, 'C');

        Assert.Equal(100, root.SizeBytes);   // the file under the cycle still counts once
        Assert.Equal(1, root.FileCount);
        Assert.Equal(2, root.FolderCount);
        Assert.Equal(100, SumOfFiles(root)); // and is reachable from the root
    }

    [Fact]
    public void Three_node_parent_cycle_is_broken_and_bytes_are_conserved()
    {
        var e = NewEntries(12);
        e[6] = Dir(6, 8, "a");
        e[7] = Dir(7, 6, "b");
        e[8] = Dir(8, 7, "c");      // a→c→b→a
        e[9] = File(9, 7, "f.bin", 250);

        var root = new MftTreeBuilder().Build(e, 'C');

        Assert.Equal(250, root.SizeBytes);
        Assert.Equal(250, SumOfFiles(root));
        Assert.Equal(3, root.FolderCount);
    }

    [Fact]
    public void Orphans_and_self_parents_are_reparented_to_root()
    {
        var e = NewEntries(10);
        e[6] = File(6, 999_999, "orphan.bin", 50);  // parent out of range
        e[7] = Dir(7, 7, "self");                   // self-parent directory
        e[8] = File(8, 7, "inside.bin", 70);

        var root = new MftTreeBuilder().Build(e, 'C');

        Assert.Equal(120, root.SizeBytes);
        Assert.Equal(120, SumOfFiles(root));
        Assert.Equal(2, root.FileCount);
    }

    [Fact]
    public void Deep_chain_capped_by_a_cycle_builds_promptly_with_bytes_conserved()
    {
        // 10,000 directories in a chain whose top two records form a cycle, each dir holding a
        // 1-byte file. Pre-fix this was O(N × 1,000,000) — effectively a hang; post-fix the
        // cycle-break pass is O(N) and the whole build finishes in test time.
        const int chain = 10_000;
        var e = NewEntries(chain * 2 + 10);

        e[6] = Dir(6, 7, "top-a");
        e[7] = Dir(7, 6, "top-b"); // the cycle
        uint prev = 6;
        for (uint i = 0; i < chain; i++)
        {
            uint dirNo = 8 + i * 2;
            e[dirNo] = Dir(dirNo, prev, $"d{i}");
            e[dirNo + 1] = File(dirNo + 1, dirNo, $"f{i}", 1);
            prev = dirNo;
        }

        var root = new MftTreeBuilder().Build(e, 'C');

        Assert.Equal(chain, root.SizeBytes);
        Assert.Equal(chain, SumOfFiles(root));
        Assert.Equal(chain, root.FileCount);
    }
}
