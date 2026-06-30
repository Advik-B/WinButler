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
        var root = BuildDirectory(rootPath, progress, ct);
        Finalize(root);
        return root;
    }

    private static DiskNode BuildDirectory(string path, Action<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Invoke(path);

        var info = new DirectoryInfo(path);
        var node = new DiskNode
        {
            Name = info.Name.Length > 0 ? info.Name : path,
            FullPath = path,
            IsDirectory = true,
            Modified = TryGetWriteTime(() => info.LastWriteTimeUtc),
        };

        // Files in this directory.
        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(path)))
        {
            ct.ThrowIfCancellationRequested();
            long length;
            DateTime? modified;
            try
            {
                var fi = new FileInfo(file);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                length = fi.Length;
                modified = fi.LastWriteTimeUtc;
            }
            catch { continue; }

            node.Children.Add(new DiskNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false,
                SizeBytes = length,
                AllocBytes = length, // no cheap allocated-size source on the fallback path
                Modified = modified,
            });

            node.SizeBytes += length;
            node.AllocBytes += length;
            node.FileCount++;
        }

        // Subdirectories.
        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(path)))
        {
            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch { continue; }

            var child = BuildDirectory(dir, progress, ct);
            node.Children.Add(child);
            node.SizeBytes += child.SizeBytes;
            node.AllocBytes += child.AllocBytes;
            node.FileCount += child.FileCount;
            node.FolderCount += child.FolderCount + 1;
        }

        return node;
    }

    /// <summary>Sorts children largest-first and fills PercentOfParent (iterative DFS).</summary>
    private static void Finalize(DiskNode root)
    {
        root.PercentOfParent = 1.0;
        var stack = new Stack<DiskNode>();
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
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate(); }
        catch { return Array.Empty<string>(); }
    }

    private static DateTime? TryGetWriteTime(Func<DateTime> get)
    {
        try { return get(); }
        catch { return null; }
    }
}
