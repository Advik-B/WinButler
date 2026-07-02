using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WinButler.Services.Mft;

/// <summary>
/// Fallback scanner for volumes/folders the MFT reader can't handle (non-NTFS: exFAT, ReFS,
/// FAT, network/USB). Walks the directory tree the ordinary way and produces the <em>same</em>
/// <see cref="DiskNode"/> shape as <see cref="MftTreeBuilder"/>, so the UI is identical — it's
/// just slower. Reparse points (junctions/symlinks) are skipped to avoid cycles and
/// double-counting, mirroring the guard in <see cref="WinButler.Services.CacheScanner"/>.
/// </summary>
public sealed class RecursiveWalkScanner
{
    public DiskNode Scan(string rootPath, Action<string>? progress = null, CancellationToken ct = default)
    {
        // Explicit work stack instead of recursion: this is the very path the MFT engine
        // degrades to on trouble, so a pathologically deep tree must not blow the stack.
        var rootInfo = new DirectoryInfo(rootPath);
        var root = NewDirectoryNode(rootInfo, rootPath);

        // (node, parent) in creation order — a parent always precedes its children, so the
        // reverse sweep below rolls every directory's totals up exactly once.
        var all = new List<(DiskNode Node, DiskNode? Parent)> { (root, null) };
        var work = new Stack<(DirectoryInfo Dir, DiskNode Node)>();
        work.Push((rootInfo, root));

        while (work.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, node) = work.Pop();
            progress?.Invoke(node.FullPath);

            // One enumeration per directory: the FileSystemInfos carry size/attributes/times
            // from the find data, so there is no second stat per file.
            try
            {
                foreach (var entry in dir.EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    if (entry is FileInfo file)
                    {
                        node.AddChild(new DiskNode
                        {
                            Name = file.Name,
                            Parent = node, // FullPath computed on demand, matching the MFT builder
                            IsDirectory = false,
                            SizeBytes = file.Length,
                            AllocBytes = file.Length, // no cheap allocated-size source on the fallback path
                            Modified = file.LastWriteTimeUtc,
                        });
                        node.SizeBytes += file.Length;
                        node.AllocBytes += file.Length;
                        node.FileCount++;
                    }
                    else if (entry is DirectoryInfo sub)
                    {
                        var child = NewDirectoryNode(sub, sub.FullName);
                        node.AddChild(child);
                        all.Add((child, node));
                        work.Push((sub, child));
                    }
                }
            }
            catch { /* skip unreadable directory contents */ }
        }

        // Bottom-up aggregation: reverse creation order visits every child before its own
        // parent is rolled up, so each directory adds its completed subtree totals once.
        for (int i = all.Count - 1; i >= 1; i--)
        {
            var (node, parent) = all[i];
            parent!.SizeBytes += node.SizeBytes;
            parent.AllocBytes += node.AllocBytes;
            parent.FileCount += node.FileCount;
            parent.FolderCount += node.FolderCount + 1;
        }

        Finalize(root);
        return root;
    }

    private static DiskNode NewDirectoryNode(DirectoryInfo info, string path) => new()
    {
        Name = info.Name.Length > 0 ? info.Name : path,
        FullPath = path,
        IsDirectory = true,
        Modified = TryGetWriteTime(() => info.LastWriteTimeUtc),
    };

    /// <summary>Sorts children largest-first and fills PercentOfParent (iterative DFS).</summary>
    private static void Finalize(DiskNode root)
    {
        root.PercentOfParent = 1.0;
        var stack = new Stack<DiskNode>();
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
    }

    private static DateTime? TryGetWriteTime(Func<DateTime> get)
    {
        try { return get(); }
        catch { return null; }
    }
}
