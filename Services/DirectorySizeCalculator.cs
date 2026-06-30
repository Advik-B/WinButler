using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WinButler.Services;

/// <summary>
/// Computes the on-disk size of a directory tree, tolerating the access errors
/// that are routine when sweeping system folders. Always call off the UI thread.
/// </summary>
public static class DirectorySizeCalculator
{
    /// <summary>
    /// Sums the length of every file under <paramref name="path"/>. Files and
    /// sub-directories that throw (access denied, in use, path too long) are skipped
    /// rather than aborting the whole walk. Does not follow reparse points (junctions/symlinks).
    /// </summary>
    public static long GetSize(string path, CancellationToken ct = default)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // Don't walk into junctions/symlinks — their contents live elsewhere.
            try
            {
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { /* skip unreadable file */ }
                }
            }
            catch { /* skip unreadable directory contents */ }

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(current))
                    stack.Push(dir);
            }
            catch { /* skip unreadable directory listing */ }
        }

        return total;
    }
}
