using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Default <see cref="ICleaner"/>. Enforces the dry-run no-op invariant and applies the
/// hybrid delete policy: <see cref="RiskLevel.Safe"/> targets are removed permanently,
/// everything else is sent to the Recycle Bin.
/// </summary>
public sealed class Cleaner : ICleaner
{
    public Task<CleanResult> CleanAsync(CleanupTarget target, bool dryRun, CancellationToken ct = default)
        => Task.Run(() => Clean(target, dryRun, ct), ct);

    private static CleanResult Clean(CleanupTarget target, bool dryRun, CancellationToken ct)
    {
        // DRY-RUN: never touch the disk. Report the projected reclaim and stop.
        if (dryRun)
        {
            return new CleanResult
            {
                Target = target,
                Succeeded = true,
                WasDryRun = true,
                BytesReclaimed = target.SizeBytes,
            };
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(target.FullPath) && !Directory.Exists(target.FullPath))
            {
                // Already gone — treat as success, nothing reclaimed now.
                return new CleanResult
                {
                    Target = target,
                    Succeeded = true,
                    WasDryRun = false,
                    BytesReclaimed = 0,
                };
            }

            switch (target.DeleteMode)
            {
                case DeleteMode.RecycleBin:
                    RecycleBin.Send(target.FullPath);
                    break;

                case DeleteMode.Permanent:
                    DeletePermanently(target.FullPath);
                    break;
            }

            // The audit trail for every real deletion (dry-run returned long before this point).
            Log.Info("clean", $"{target.DeleteMode}: {target.FullPath} ({target.SizeBytes} B) — ok");
            return new CleanResult
            {
                Target = target,
                Succeeded = true,
                WasDryRun = false,
                BytesReclaimed = target.SizeBytes,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("clean", $"{target.DeleteMode}: {target.FullPath} — failed", ex);
            return new CleanResult
            {
                Target = target,
                Succeeded = false,
                WasDryRun = false,
                BytesReclaimed = 0,
                Error = ex is UnauthorizedAccessException or IOException
                    ? $"Skipped: {ex.Message}"
                    : ex.Message,
            };
        }
    }

    private static void DeletePermanently(string path)
    {
        if (Directory.Exists(path))
        {
            // ReparsePoint guard: never recurse through a junction/symlink.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(path, recursive: false);
            else
                Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
