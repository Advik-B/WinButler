using System.Collections.Generic;
using System.IO;
using System.Threading;
using WinButler.Services.Mft;

namespace WinButler.Services;

/// <summary>
/// Computes the on-disk size of a directory tree, tolerating the access errors
/// that are routine when sweeping system folders. Always call off the UI thread.
/// </summary>
public static class DirectorySizeCalculator
{
    /// <summary>
    /// The app-wide shared disk index, set once at startup (see <c>MainWindowViewModel</c>). When a
    /// queried folder is present in it, its aggregate size is returned in O(1) instead of walking —
    /// this is what makes every feature scan reuse one MFT read. Left null in tests, which then take
    /// the live-walk path below unchanged.
    /// </summary>
    public static DiskIndexService? Index { get; set; }

    /// <summary>
    /// Sums the length of every file under <paramref name="path"/>. Files and
    /// sub-directories that throw (access denied, in use, path too long) are skipped
    /// rather than aborting the whole walk. Does not follow reparse points (junctions/symlinks).
    /// Consults the shared <see cref="Index"/> first; a hit avoids the walk entirely.
    /// </summary>
    public static long GetSize(string path, CancellationToken ct = default)
    {
        // Reuse the shared whole-volume index when it covers this path (junction contents live
        // elsewhere, so an indexed junction reports ~0 — matching the walk's reparse-point skip).
        var indexed = Index?.GetSize(path);
        if (indexed is not null)
            return indexed.Value;

        // Don't walk into a junction/symlink root — its contents live elsewhere.
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return 0;
        }
        catch
        {
            return 0;
        }

        long total = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(path));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // One enumeration per directory: the FileSystemInfos carry size and attributes
            // from the find data, so there is no second stat per file (which used to double
            // the syscall count), and the reparse-point check comes free per entry.
            try
            {
                foreach (var entry in current.EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if (entry is FileInfo file)
                        total += file.Length;
                    else if (entry is DirectoryInfo dir)
                        stack.Push(dir);
                }
            }
            catch { /* skip unreadable directory contents */ }
        }

        return total;
    }
}
