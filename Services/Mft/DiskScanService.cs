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
/// </summary>
public sealed class DiskScanService
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
    public Task<DiskNode> ScanAsync(string target, IProgress<string>? progress = null, CancellationToken ct = default)
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
            catch
            {
                // MFT path failed (access, exotic geometry) — degrade gracefully to a walk.
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
