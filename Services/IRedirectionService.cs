using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Relocates large redirectable directories to another drive, leaving a junction behind so
/// applications keep working transparently. Every mutating step honours dry-run and the
/// validate → copy → verify → delete → junction → ledger ordering.
/// </summary>
public interface IRedirectionService
{
    Task<IReadOnlyList<RedirectCandidate>> ScanCandidatesAsync(CancellationToken ct = default);

    /// <summary>Cancellation is honoured only at safe points (before copy, before the original
    /// is deleted) — never between delete-original and junction+ledger.</summary>
    Task<RedirectResult> RedirectAsync(RedirectCandidate candidate, string driveLetter, bool dryRun,
        CancellationToken ct = default);

    /// <summary>Cancellation is honoured only before the junction is removed; the restore then
    /// runs to completion.</summary>
    Task<RedirectResult> UndoAsync(RedirectRecord record, bool dryRun, CancellationToken ct = default);

    /// <summary>Currently-active redirects, from the persisted ledger.</summary>
    IReadOnlyList<RedirectRecord> GetActiveRedirects();

    /// <summary>Folders under any eligible drive's \_redirected\ root that no ledger record
    /// points at (crash between move and ledger write). Report-only — never auto-repaired.</summary>
    IReadOnlyList<string> FindOrphanedRedirects();

    /// <summary>Fixed NTFS drive letters available as targets (e.g. "D", "S").</summary>
    IReadOnlyList<string> GetEligibleDrives();

    /// <summary>The fixed NTFS drive with the most free space, or null.</summary>
    string? SuggestTargetDrive();
}
