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
                // FullPath: assigned in the path pass for directories; files get a Parent
                // reference instead and compute theirs on demand.
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

        // 2b) Break parent cycles (A→B→A — both records individually valid, so step 2 kept
        //     them). A cycle would make the climb in step 4 spin to its guard for EVERY node
        //     beneath it and leave the whole subtree unreachable from the root. Walk each
        //     node's ancestor chain, stamping with the origin index: re-entering a stamp from
        //     the same walk closes a cycle — reparent that node to root. Hitting an older
        //     stamp joins a chain already proven to terminate. Each node is stamped once, so
        //     the whole pass is O(N).
        var stamp = new int[entries.Length];
        Array.Fill(stamp, -1);
        for (int i = 0; i < entries.Length; i++)
        {
            if (nodes[i] is null)
                continue;
            int cur = i;
            while (cur >= 0 && cur != RootRecord)
            {
                if (stamp[cur] == i)
                {
                    parentIdx[cur] = RootRecord;
                    break;
                }
                if (stamp[cur] != -1)
                    break;
                stamp[cur] = i;
                cur = parentIdx[cur];
            }
        }

        // 3) Link children.
        for (int i = 0; i < entries.Length; i++)
        {
            int p = parentIdx[i];
            if (p < 0 || nodes[i] is null)
                continue;
            nodes[p]!.AddChild(nodes[i]!);
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

            // Step 2b guarantees every chain terminates at the root, so this loop is bounded
            // by real tree depth; the guard is purely defensive (65536 clears the deepest
            // path NTFS can legally express — ~16K components — by 4×).
            int cur = parentIdx[i];
            int guard = 0;
            while (cur >= 0 && guard++ < 65_536)
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

        // 5) Reconstruct full paths top-down (iterative DFS). Only directories STORE a path
        //    (the DriveIndex keys them); the far more numerous file leaves keep a Parent
        //    reference and compute theirs on demand — on a whole drive this is the single
        //    biggest retained-memory cost in the app.
        root.FullPath = driveRoot;
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            string prefix = n.FullPath.EndsWith('\\') ? n.FullPath : n.FullPath + '\\';
            foreach (var child in n.Children)
            {
                if (child.IsDirectory)
                {
                    child.FullPath = prefix + child.Name;
                    stack.Push(child);
                }
                else
                {
                    child.Parent = n;
                }
            }
        }

        // 6) Sort children largest-first and fill PercentOfParent.
        root.PercentOfParent = 1.0;
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            n.SortChildren(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            foreach (var child in n.Children)
            {
                child.PercentOfParent = n.SizeBytes > 0 ? (double)child.SizeBytes / n.SizeBytes : 0;
                if (child.HasChildren)
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
