using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinButler.Services.Mft;

/// <summary>A scannable volume, surfaced to the UI's drive picker.</summary>
public sealed record ScanDrive(char Letter, string Format, bool IsNtfs, string DisplayName)
{
    public string RootPath => $"{Letter}:\\";
}

/// <summary>
/// Front door the disk-usage page talks to. Hides the choice between the fast NTFS MFT path
/// (<see cref="MftReader"/> + <see cref="MftTreeBuilder"/>) and the
/// <see cref="RecursiveWalkScanner"/> fallback, and handles "scan just this folder" by reading
/// the whole volume's MFT and re-rooting the tree at the subpath — exactly what WizTree does.
/// <see cref="ScanAsync"/> is virtual as the test seam: headless tests substitute a canned
/// tree so index-building commands can run without touching a real disk.
/// </summary>
public class DiskScanService
{
    /// <summary>Fixed/removable volumes that are ready, for the drive dropdown.</summary>
    public IReadOnlyList<ScanDrive> GetScannableDrives()
    {
        var drives = new List<ScanDrive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady)
                    continue;

                char letter = char.ToUpperInvariant(d.Name[0]);
                string format = d.DriveFormat;
                bool isNtfs = string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase);
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $"({d.VolumeLabel}) ";
                string display =
                    $"{letter}: {label}— {format}, " +
                    $"{SizeFormatter.Format(d.AvailableFreeSpace)} free of {SizeFormatter.Format(d.TotalSize)}";

                drives.Add(new ScanDrive(letter, format, isNtfs, display));
            }
            catch
            {
                // Drive went away or denied between enumeration and query — skip it.
            }
        }
        return drives;
    }

    /// <summary>
    /// Scans a volume root (e.g. <c>C:\</c>) or any folder, off the UI thread.
    /// <paramref name="progress"/> receives human-readable status lines.
    /// </summary>
    public virtual Task<DiskNode> ScanAsync(string target, IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(target, progress, ct), ct);

    private DiskNode Scan(string target, IProgress<string>? progress, CancellationToken ct)
    {
        target = Path.GetFullPath(target);
        if (target.Length < 2 || target[1] != ':')
            return Walk(target, progress, ct); // UNC/network path — walk it.

        char letter = char.ToUpperInvariant(target[0]);
        bool isVolumeRoot = string.Equals(target, Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);

        if (IsNtfs(letter))
        {
            try
            {
                progress?.Report($"Reading the master file table of {letter}: …");
                var reader = new MftReader();
                var entries = reader.Read(
                    letter,
                    (done, total) => progress?.Report($"Parsing MFT — {done:N0} / {total:N0} records"),
                    ct);

                if (reader.SkippedRecords > 0)
                {
                    // Below the reader's corruption threshold: results are complete except for
                    // the skipped records. Say so — a silently-partial total is worse.
                    Log.Warn("mft", $"{reader.SkippedRecords} unreadable MFT record(s) skipped on {letter}:.");
                    progress?.Report($"{reader.SkippedRecords:N0} unreadable MFT record(s) skipped.");
                }

                progress?.Report("Building the directory tree…");
                var root = new MftTreeBuilder().Build(entries, letter);

                if (isVolumeRoot)
                    return root;

                var sub = FindNode(root, target);
                if (sub is not null)
                    return sub;
                // Subpath not present in the MFT tree (rare) — fall through to a direct walk.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // MFT path failed (access, exotic geometry, corruption past the reader's
                // threshold) — degrade gracefully to a walk, but never silently: the walk is
                // minutes where the MFT is seconds, and that cliff must be diagnosable.
                Log.Warn("mft", $"MFT read of {letter}: failed — falling back to a directory walk (slower).", ex);
                progress?.Report("MFT read failed — falling back to a directory walk (slower)…");
            }
        }

        return Walk(target, progress, ct);
    }

    private static DiskNode Walk(string target, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"Scanning {target} …");
        return new RecursiveWalkScanner().Scan(
            target,
            p => progress?.Report($"Scanning {p}"),
            ct);
    }

    private static bool IsNtfs(char letter)
    {
        try { return string.Equals(new DriveInfo($"{letter}:\\").DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>Walks the built tree to the node matching <paramref name="fullPath"/>, or null.</summary>
    private static DiskNode? FindNode(DiskNode root, string fullPath)
    {
        string rel = Path.GetRelativePath(root.FullPath, fullPath);
        if (rel == "." || rel.Length == 0)
            return root;

        var cur = root;
        foreach (var part in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cur = cur.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (cur is null)
                return null;
        }
        return cur;
    }
}
