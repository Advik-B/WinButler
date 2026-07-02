using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

public sealed class RedirectionService : IRedirectionService
{
    private const string RedirectFolder = "_redirected";

    // The redirectable catalog and deny list come from definitions.json (the maintainable source
    // of truth), injected as a RedirectRuleSet so remote/online definitions can extend them.
    private readonly RedirectRuleSet _rules;
    private readonly string _ledgerPath;
    private List<RedirectRecord> _ledger;

    public RedirectionService(RedirectRuleSet rules)
        : this(rules, DefaultLedgerPath()) { }

    /// <summary>Test seam: an explicit ledger location, so tests never touch the real ledger.</summary>
    internal RedirectionService(RedirectRuleSet rules, string ledgerPath)
    {
        _rules = rules;
        _ledgerPath = ledgerPath;
        _ledger = LoadLedger(_ledgerPath);
    }

    private static string DefaultLedgerPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinButler", "redirects.json");

    /// <summary>Convenience overload using the bundled definitions (tests/standalone).</summary>
    public RedirectionService()
        : this(Definitions.BundledDefinitionSource.Load().Redirect) { }

    // ── Discovery ───────────────────────────────────────────────────────────────────
    public Task<IReadOnlyList<RedirectCandidate>> ScanCandidatesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<RedirectCandidate>();

        foreach (var entry in _rules.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (_rules.DenyNames.Any(d => entry.RelativeToProfile.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                continue;

            var source = Path.Combine(profile, entry.RelativeToProfile);
            if (!Directory.Exists(source))
                continue;

            bool isJunction = Junction.IsJunction(source);
            list.Add(new RedirectCandidate
            {
                SourcePath = source,
                DisplayName = entry.DisplayName,
                Description = entry.Description,
                Category = entry.Category,
                TargetName = entry.TargetName,
                SizeBytes = isJunction ? 0 : DirectorySizeCalculator.GetSize(source),
                IsAlreadyRedirected = isJunction,
                ExistingTarget = isJunction ? Junction.GetTarget(source) : null,
            });
        }

        return (IReadOnlyList<RedirectCandidate>)list.OrderByDescending(c => c.SizeBytes).ToList();
    }, ct);

    // ── Redirect ────────────────────────────────────────────────────────────────────
    public Task<RedirectResult> RedirectAsync(RedirectCandidate candidate, string driveLetter, bool dryRun,
        CancellationToken ct = default)
        => Task.Run(() => Redirect(candidate, driveLetter, dryRun, ct), ct);

    private RedirectResult Redirect(RedirectCandidate candidate, string driveLetter, bool dryRun, CancellationToken ct)
    {
        var source = candidate.SourcePath;

        // ── 1. Validate ──
        if (Junction.IsJunction(source))
            return Fail(dryRun, $"{candidate.DisplayName} is already redirected.");
        if (!Directory.Exists(source))
            return Fail(dryRun, $"Source no longer exists: {source}");

        var driveError = ValidateDrive(driveLetter, candidate.SizeBytes);
        if (driveError != null)
            return Fail(dryRun, driveError);

        var redirectRoot = Path.Combine($"{driveLetter}:\\", RedirectFolder);
        var dest = Path.Combine(redirectRoot, candidate.TargetName);

        // This inspection runs for dry-run too — a denied/unreadable dest must fail the
        // validation, not escape as an exception.
        bool destHasData;
        try
        {
            destHasData = Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any();
        }
        catch (Exception ex)
        {
            return Fail(dryRun, $"Cannot inspect target {dest} ({ex.Message}).");
        }
        if (destHasData)
            return Fail(dryRun, $"Target already exists and is not empty: {dest}");

        if (dryRun)
        {
            return new RedirectResult
            {
                Succeeded = true,
                WasDryRun = true,
                BytesMoved = candidate.SizeBytes,
                Message = $"DRY RUN — would move {candidate.DisplayName} " +
                          $"({SizeFormatter.Format(candidate.SizeBytes)}) to {dest} and create a junction.",
            };
        }

        // ── 2. Copy ──
        ct.ThrowIfCancellationRequested();
        Log.Info("redirect", $"Redirecting {source} → {dest} ({SizeFormatter.Format(candidate.SizeBytes)}).");
        try
        {
            Directory.CreateDirectory(redirectRoot);
            RunRobocopy(source, dest, ct);
        }
        catch (OperationCanceledException)
        {
            TryDelete(dest);
            Log.Info("redirect", $"Redirect of {source} cancelled during copy; partial copy removed, original untouched.");
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            Log.Warn("redirect", $"Copy failed for {source}; original untouched.", ex);
            return Fail(false, $"Copy failed ({ex.Message}). Original left untouched.");
        }

        // ── 3. Verify (before deleting ANYTHING) ──
        var (srcFiles, srcBytes) = Measure(source);
        var (dstFiles, dstBytes) = Measure(dest);
        if (srcFiles != dstFiles || srcBytes != dstBytes)
        {
            TryDelete(dest);
            Log.Warn("redirect",
                $"Verification failed for {source}: src {srcFiles} files/{srcBytes} B vs dst {dstFiles}/{dstBytes} B.");
            return Fail(false,
                $"Verification failed (src {srcFiles} files/{SizeFormatter.Format(srcBytes)} vs " +
                $"dst {dstFiles}/{SizeFormatter.Format(dstBytes)}). Partial copy removed; original untouched.");
        }

        // ── 4. Delete original ── (LAST cancellation checkpoint: from here to the ledger
        // write the operation must run to completion, or the data would be orphaned.)
        if (ct.IsCancellationRequested)
        {
            TryDelete(dest);
            Log.Info("redirect", $"Redirect of {source} cancelled before commit; copy removed, original untouched.");
            ct.ThrowIfCancellationRequested();
        }
        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            Log.Warn("redirect", $"Could not remove original {source}; copy removed, original untouched.", ex);
            return Fail(false, $"Could not remove original ({ex.Message}). Copy removed; original untouched.");
        }

        // ── 5. Create junction (original is gone now; recover by moving data back on failure) ──
        try
        {
            Junction.Create(source, dest);
        }
        catch (Exception ex)
        {
            // Recovery is itself fallible — report what actually happened, never claim a
            // restore that didn't complete (the data is always intact at dest either way).
            bool restored = false;
            try
            {
                RunRobocopy(dest, source);
                TryDelete(dest);
                restored = true;
            }
            catch (Exception restoreEx)
            {
                Log.Error("redirect", $"Restore after junction failure ALSO failed for {source}; data is at {dest}.", restoreEx);
            }
            Log.Error("redirect", $"Junction creation failed for {source} (restored={restored}).", ex);
            return Fail(false, restored
                ? $"Junction creation failed ({ex.Message}). Data restored to original location."
                : $"Junction creation failed ({ex.Message}) AND restoring failed — your data is intact at {dest}, " +
                  $"but the original location is missing. See the log for details.");
        }

        // ── 6. Ledger (only after the junction exists) ──
        var record = new RedirectRecord
        {
            SourcePath = source,
            TargetPath = dest,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            SizeBytes = srcBytes,
        };
        _ledger.Add(record);
        SaveLedger();

        Log.Info("redirect", $"Redirected {source} → {dest} ({srcBytes} B); junction created, ledger updated.");
        return new RedirectResult
        {
            Succeeded = true,
            WasDryRun = false,
            BytesMoved = srcBytes,
            Message = $"Redirected {candidate.DisplayName} → {dest} " +
                      $"({SizeFormatter.Format(srcBytes)} freed on {Path.GetPathRoot(source)}).",
        };
    }

    // ── Undo ────────────────────────────────────────────────────────────────────────
    public Task<RedirectResult> UndoAsync(RedirectRecord record, bool dryRun, CancellationToken ct = default)
        => Task.Run(() => Undo(record, dryRun, ct), ct);

    private RedirectResult Undo(RedirectRecord record, bool dryRun, CancellationToken ct)
    {
        var source = record.SourcePath;
        var dest = record.TargetPath;

        if (!Junction.IsJunction(source))
            return Fail(dryRun, $"{source} is not a junction; cannot undo automatically.");
        if (!Directory.Exists(dest))
            return Fail(dryRun, $"Redirected data not found at {dest}.");

        if (dryRun)
        {
            return new RedirectResult
            {
                Succeeded = true,
                WasDryRun = true,
                BytesMoved = record.SizeBytes,
                Message = $"DRY RUN — would remove the junction and move data back from {dest} to {source}.",
            };
        }

        // Only cancellation checkpoint: once the junction is removed, the restore must run
        // to completion (the data is only reachable at dest until it's copied back).
        ct.ThrowIfCancellationRequested();

        // Remove the junction (does NOT touch the target data).
        Log.Info("redirect", $"Undoing redirect: {dest} → {source}.");
        try { Junction.Remove(source); }
        catch (Exception ex)
        {
            Log.Warn("redirect", $"Could not remove junction at {source}.", ex);
            return Fail(false, $"Could not remove junction ({ex.Message}).");
        }

        // Move data back, then verify before deleting the target copy.
        try
        {
            RunRobocopy(dest, source);
            var (df, db) = Measure(dest);
            var (sf, sb) = Measure(source);
            if (df != sf || db != sb)
            {
                Log.Warn("redirect", $"Undo verification failed for {source}; data preserved at {dest}.");
                return Fail(false, $"Restore verification failed; data preserved at {dest}. Junction removed.");
            }

            TryDelete(dest);
        }
        catch (Exception ex)
        {
            Log.Warn("redirect", $"Undo copy failed for {source}; data preserved at {dest}.", ex);
            return Fail(false, $"Restore copy failed ({ex.Message}); data preserved at {dest}.");
        }

        _ledger.RemoveAll(r => string.Equals(r.SourcePath, source, StringComparison.OrdinalIgnoreCase));
        SaveLedger();

        Log.Info("redirect", $"Undo complete: {source} restored, ledger updated.");
        return new RedirectResult
        {
            Succeeded = true,
            WasDryRun = false,
            BytesMoved = record.SizeBytes,
            Message = $"Restored {Path.GetFileName(source)} back to {source}.",
        };
    }

    // ── Drives ──────────────────────────────────────────────────────────────────────
    public IReadOnlyList<RedirectRecord> GetActiveRedirects() => _ledger.ToList();

    public IReadOnlyList<string> GetEligibleDrives()
    {
        // DriveInfo.GetDrives itself can throw on transient volumes — degrade to "none".
        try
        {
            return DriveInfo.GetDrives()
                .Where(IsEligible)
                .Select(d => d.Name.Substring(0, 1))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public string? SuggestTargetDrive()
    {
        try
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory)?.Substring(0, 1);
            return DriveInfo.GetDrives()
                .Where(IsEligible)
                .Where(d => !string.Equals(d.Name.Substring(0, 1), system, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.AvailableFreeSpace)
                .Select(d => d.Name.Substring(0, 1))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsEligible(DriveInfo d)
    {
        try { return d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat == "NTFS"; }
        catch { return false; }
    }

    private string? ValidateDrive(string driveLetter, long needBytes)
    {
        DriveInfo? di;
        try
        {
            di = DriveInfo.GetDrives()
                .FirstOrDefault(d => string.Equals(d.Name.Substring(0, 1), driveLetter, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            return $"Could not query drives ({ex.Message}).";
        }
        if (di == null || !di.IsReady)
            return $"Drive {driveLetter}: is not available.";
        if (di.DriveType != DriveType.Fixed)
            return $"Drive {driveLetter}: is not a fixed disk — junctions need a local fixed NTFS volume.";
        if (di.DriveFormat != "NTFS")
            return $"Drive {driveLetter}: is {di.DriveFormat}, not NTFS.";
        if (di.AvailableFreeSpace < needBytes + (64L * 1024 * 1024))
            return $"Drive {driveLetter}: has insufficient free space.";
        return null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────
    private static void RunRobocopy(string src, string dst, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "robocopy",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { src, dst, "/E", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;

        // Event-driven reads: sequentially ReadToEnd-ing two redirected pipes deadlocks when the
        // child fills the un-drained one. Keep a bounded stderr tail for the failure message.
        var stderrTail = new List<string>();
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderrTail)
                    if (stderrTail.Count < 20)
                        stderrTail.Add(e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Poll-wait so cancellation can kill the copy; no hard timeout — killing a legitimate
        // multi-hundred-GB copy on a timer would be harmful. Cancellation is the user's timeout.
        while (!proc.WaitForExit(500))
        {
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                proc.WaitForExit();
                ct.ThrowIfCancellationRequested();
            }
        }
        proc.WaitForExit(); // parameterless overload also drains the async output handlers

        // Robocopy exit codes: 0-7 = success (files copied / nothing to do), >=8 = failure.
        if (proc.ExitCode >= 8)
        {
            string tail;
            lock (stderrTail)
                tail = stderrTail.Count > 0 ? $" — {string.Join(" | ", stderrTail)}" : "";
            throw new IOException($"robocopy exit code {proc.ExitCode}{tail}");
        }
    }

    private static (int files, long bytes) Measure(string path)
    {
        long bytes = 0;
        int files = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(cur))
                {
                    try { bytes += new FileInfo(f).Length; files++; } catch { }
                }
                foreach (var d in Directory.EnumerateDirectories(cur))
                    stack.Push(d);
            }
            catch { }
        }
        return (files, bytes);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    private static RedirectResult Fail(bool dryRun, string message) =>
        new() { Succeeded = false, WasDryRun = dryRun, Message = message };

    private static List<RedirectRecord> LoadLedger(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<RedirectRecord>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            // Keep the damaged file — its undo records may still be recoverable by hand,
            // and the next SaveLedger would otherwise overwrite the only copy.
            Log.Error("redirect", $"Ledger at {path} is unreadable; preserving a copy as .corrupt.", ex);
            try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
        }
        return new List<RedirectRecord>();
    }

    private void SaveLedger()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
            // Write-temp-then-move so a crash mid-write can never corrupt the only ledger copy.
            var tmp = _ledgerPath + ".tmp";
            File.WriteAllText(tmp,
                JsonSerializer.Serialize(_ledger, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _ledgerPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("redirect", "Could not save the redirect ledger.", ex);
        }
    }

    // ── Orphan reconciliation ───────────────────────────────────────────────────────
    public IReadOnlyList<string> FindOrphanedRedirects()
    {
        var roots = GetEligibleDrives().Select(d => Path.Combine($"{d}:\\", RedirectFolder));
        return FindOrphanedRedirects(roots);
    }

    /// <summary>
    /// Detects folders under a drive's \_redirected\ root that no ledger record points at — the
    /// fingerprint of a crash between delete-original and ledger write. Report-only by design:
    /// the data is intact, and deciding what to do with it is the user's call, not ours.
    /// </summary>
    internal IReadOnlyList<string> FindOrphanedRedirects(IEnumerable<string> redirectRoots)
    {
        var known = new HashSet<string>(
            _ledger.Select(r => Path.TrimEndingDirectorySeparator(r.TargetPath)),
            StringComparer.OrdinalIgnoreCase);

        var orphans = new List<string>();
        foreach (var root in redirectRoots)
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var dir in Directory.EnumerateDirectories(root))
                    if (!known.Contains(Path.TrimEndingDirectorySeparator(dir)))
                        orphans.Add(dir);
            }
            catch { /* unreadable root — skip */ }
        }

        if (orphans.Count > 0)
            Log.Warn("redirect",
                $"{orphans.Count} orphaned redirect folder(s) with no ledger record: {string.Join("; ", orphans)}");
        return orphans;
    }
}
