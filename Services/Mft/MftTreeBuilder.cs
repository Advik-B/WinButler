using System;
using System.Collections.Generic;

namespace WinButler.Services.Mft;

/// <summary>
/// Turns the flat <see cref="MftEntry"/> array from <see cref="MftReader"/> into an aggregated
/// <see cref="DiskNode"/> tree: links each record to its parent by record number, reconstructs
/// full paths, and rolls descendant file sizes/counts up to every ancestor. All loops are
/// iterative (no recursion) so the giant trees a full-drive scan produces can't blow the stack.
/// </summary>
public sealed class MftTreeBuilder
{
    // NTFS reserves record 5 for the volume root directory.
    private const int RootRecord = 5;

    public DiskNode Build(MftEntry[] entries, char driveLetter)
    {
        string driveRoot = $"{char.ToUpperInvariant(driveLetter)}:\\";
        var nodes = new DiskNode?[entries.Length];

        // 1) Materialize a node per in-use record (own size only for now).
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (!e.InUse)
                continue;

            nodes[i] = new DiskNode
            {
                Name = e.Name,
                FullPath = string.Empty, // assigned in the path pass
                IsDirectory = e.IsDirectory,
                SizeBytes = e.RealSize,
                AllocBytes = e.AllocSize,
                Modified = ToDateTime(e.ModifiedTicks),
            };
        }

        DiskNode root = nodes[RootRecord] ?? new DiskNode { Name = driveRoot, FullPath = driveRoot, IsDirectory = true };
        root.Name = driveRoot;
        nodes[RootRecord] = root;

        // 2) Resolve each node's parent index once. Orphans (parent missing/out of range) and any
        //    self-parent are reparented to root so their bytes still count toward the total.
        var parentIdx = new int[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            if (nodes[i] is null || i == RootRecord)
            {
                parentIdx[i] = -1;
                continue;
            }

            uint p = entries[i].ParentRecordNo;
            parentIdx[i] = (p < nodes.Length && nodes[p] is not null && p != i) ? (int)p : RootRecord;
        }

        // 3) Link children.
        for (int i = 0; i < entries.Length; i++)
        {
            int p = parentIdx[i];
            if (p < 0 || nodes[i] is null)
                continue;
            nodes[p]!.Children.Add(nodes[i]!);
        }

        // 4) Aggregate: each node contributes its own size and a +1 count to every ancestor.
        //    Climbing per-node is O(N · depth); depth is tiny, and it avoids post-order recursion.
        for (int i = 0; i < entries.Length; i++)
        {
            var n = nodes[i];
            if (n is null || i == RootRecord)
                continue;

            // Use the intrinsic own-size from the entry, NOT n.SizeBytes — a directory's
            // SizeBytes has already accumulated its children by now, and re-propagating that
            // would double-count. (Files: own size; directories: 0.)
            long sz = entries[i].RealSize;
            long al = entries[i].AllocSize;
            bool isFile = !n.IsDirectory;

            int cur = parentIdx[i];
            int guard = 0;
            while (cur >= 0 && guard++ < 1_000_000)
            {
                var p = nodes[cur];
                if (p is null)
                    break;

                p.SizeBytes += sz;
                p.AllocBytes += al;
                if (isFile) p.FileCount++; else p.FolderCount++;

                if (cur == RootRecord)
                    break;
                cur = parentIdx[cur];
            }
        }

        // 5) Reconstruct full paths top-down (iterative DFS).
        root.FullPath = driveRoot;
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            string prefix = n.FullPath.EndsWith('\\') ? n.FullPath : n.FullPath + '\\';
            foreach (var child in n.Children)
            {
                child.FullPath = prefix + child.Name;
                stack.Push(child);
            }
        }

        // 6) Sort children largest-first and fill PercentOfParent.
        root.PercentOfParent = 1.0;
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            n.Children.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            foreach (var child in n.Children)
            {
                child.PercentOfParent = n.SizeBytes > 0 ? (double)child.SizeBytes / n.SizeBytes : 0;
                stack.Push(child);
            }
        }

        return root;
    }

    private static DateTime? ToDateTime(long fileTimeUtc)
    {
        if (fileTimeUtc <= 0)
            return null;
        try { return DateTime.FromFileTimeUtc(fileTimeUtc); }
        catch { return null; }
    }
}
