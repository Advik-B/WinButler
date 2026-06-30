using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Flags the contents of the well-known temp directories. Each direct child (file or
/// folder) of a temp root becomes its own target so the user can deselect anything in use.
/// </summary>
public sealed class TempScanner : IScanner
{
    public CleanupCategory Category => CleanupCategory.Temp;
    public string Title => "Temporary files";

    public Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<CleanupTarget>>(() => Scan(ct), ct);

    private static IReadOnlyList<CleanupTarget> Scan(CancellationToken ct)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Use a set to avoid double-counting when %TEMP% already resolves to LocalAppData\Temp.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath().TrimEnd('\\'),
            Path.Combine(localAppData, "Temp"),
            Path.Combine(windir, "Temp"), // requires admin
        };

        var results = new List<CleanupTarget>();

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
                continue;

            var rootLabel = Path.GetFileName(Path.GetDirectoryName(root) ?? root) is { Length: > 0 } p
                ? $"{p}\\Temp"
                : root;

            foreach (var entry in SafeEnumerateEntries(root))
            {
                ct.ThrowIfCancellationRequested();
                long size;
                try
                {
                    size = Directory.Exists(entry)
                        ? DirectorySizeCalculator.GetSize(entry, ct)
                        : new FileInfo(entry).Length;
                }
                catch { continue; }

                results.Add(new CleanupTarget
                {
                    FullPath = entry,
                    DisplayName = $"{rootLabel}\\{Path.GetFileName(entry)}",
                    Category = CleanupCategory.Temp,
                    SizeBytes = size,
                    Risk = RiskLevel.Safe, // temp is regenerable by definition
                    Reason = "Temporary file/folder",
                });
            }
        }

        return results;
    }

    private static IEnumerable<string> SafeEnumerateEntries(string path)
    {
        try { return Directory.EnumerateFileSystemEntries(path); }
        catch { return Enumerable.Empty<string>(); }
    }
}
