using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    {
        _rules = rules;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinButler");
        _ledgerPath = Path.Combine(dir, "redirects.json");
        _ledger = LoadLedger(_ledgerPath);
    }

    /// <summary>Convenience overload using the bundled definitions (tests/standalone).</summary>
    public RedirectionService()
        : this(Definitions.BundledDefinitionSource.Load().Redirect) { }

    // ── Discovery ───────────────────────────────────────────────────────────────────
    public Task<IReadOnlyList<RedirectCandidate>> ScanCandidatesAsync() => Task.Run(() =>
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<RedirectCandidate>();

        foreach (var entry in _rules.Entries)
        {
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
    });

    // ── Redirect ────────────────────────────────────────────────────────────────────
    public Task<RedirectResult> RedirectAsync(RedirectCandidate candidate, string driveLetter, bool dryRun)
        => Task.Run(() => Redirect(candidate, driveLetter, dryRun));

    private RedirectResult Redirect(RedirectCandidate candidate, string driveLetter, bool dryRun)
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

        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
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
        Directory.CreateDirectory(redirectRoot);
        try
        {
            RunRobocopy(source, dest);
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            return Fail(false, $"Copy failed ({ex.Message}). Original left untouched.");
        }

        // ── 3. Verify (before deleting ANYTHING) ──
        var (srcFiles, srcBytes) = Measure(source);
        var (dstFiles, dstBytes) = Measure(dest);
        if (srcFiles != dstFiles || srcBytes != dstBytes)
        {
            TryDelete(dest);
            return Fail(false,
                $"Verification failed (src {srcFiles} files/{SizeFormatter.Format(srcBytes)} vs " +
                $"dst {dstFiles}/{SizeFormatter.Format(dstBytes)}). Partial copy removed; original untouched.");
        }

        // ── 4. Delete original ──
        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            return Fail(false, $"Could not remove original ({ex.Message}). Copy removed; original untouched.");
        }

        // ── 5. Create junction (original is gone now; recover by moving data back on failure) ──
        try
        {
            Junction.Create(source, dest);
        }
        catch (Exception ex)
        {
            try { RunRobocopy(dest, source); TryDelete(dest); } catch { /* data still safe in dest */ }
            return Fail(false, $"Junction creation failed ({ex.Message}). Data restored to original location.");
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
    public Task<RedirectResult> UndoAsync(RedirectRecord record, bool dryRun)
        => Task.Run(() => Undo(record, dryRun));

    private RedirectResult Undo(RedirectRecord record, bool dryRun)
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

        // Remove the junction (does NOT touch the target data).
        try { Junction.Remove(source); }
        catch (Exception ex) { return Fail(false, $"Could not remove junction ({ex.Message})."); }

        // Move data back, then verify before deleting the target copy.
        try
        {
            RunRobocopy(dest, source);
            var (df, db) = Measure(dest);
            var (sf, sb) = Measure(source);
            if (df != sf || db != sb)
                return Fail(false, $"Restore verification failed; data preserved at {dest}. Junction removed.");

            TryDelete(dest);
        }
        catch (Exception ex)
        {
            return Fail(false, $"Restore copy failed ({ex.Message}); data preserved at {dest}.");
        }

        _ledger.RemoveAll(r => string.Equals(r.SourcePath, source, StringComparison.OrdinalIgnoreCase));
        SaveLedger();

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

    public IReadOnlyList<string> GetEligibleDrives() =>
        DriveInfo.GetDrives()
            .Where(IsEligible)
            .Select(d => d.Name.Substring(0, 1))
            .ToList();

    public string? SuggestTargetDrive()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory)?.Substring(0, 1);
        return DriveInfo.GetDrives()
            .Where(IsEligible)
            .Where(d => !string.Equals(d.Name.Substring(0, 1), system, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.AvailableFreeSpace)
            .Select(d => d.Name.Substring(0, 1))
            .FirstOrDefault();
    }

    private static bool IsEligible(DriveInfo d)
    {
        try { return d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat == "NTFS"; }
        catch { return false; }
    }

    private string? ValidateDrive(string driveLetter, long needBytes)
    {
        var di = DriveInfo.GetDrives()
            .FirstOrDefault(d => string.Equals(d.Name.Substring(0, 1), driveLetter, StringComparison.OrdinalIgnoreCase));
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
    private static void RunRobocopy(string src, string dst)
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
        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        // Robocopy exit codes: 0-7 = success (files copied / nothing to do), >=8 = failure.
        if (proc.ExitCode >= 8)
            throw new IOException($"robocopy exit code {proc.ExitCode}");
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
        catch { /* corrupt/missing ledger → start fresh */ }
        return new List<RedirectRecord>();
    }

    private void SaveLedger()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
            File.WriteAllText(_ledgerPath,
                JsonSerializer.Serialize(_ledger, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal: ledger is a convenience for undo */ }
    }
}
