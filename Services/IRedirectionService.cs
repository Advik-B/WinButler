using System.Collections.Generic;
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
    Task<IReadOnlyList<RedirectCandidate>> ScanCandidatesAsync();

    Task<RedirectResult> RedirectAsync(RedirectCandidate candidate, string driveLetter, bool dryRun);

    Task<RedirectResult> UndoAsync(RedirectRecord record, bool dryRun);

    /// <summary>Currently-active redirects, from the persisted ledger.</summary>
    IReadOnlyList<RedirectRecord> GetActiveRedirects();

    /// <summary>Fixed NTFS drive letters available as targets (e.g. "D", "S").</summary>
    IReadOnlyList<string> GetEligibleDrives();

    /// <summary>The fixed NTFS drive with the most free space, or null.</summary>
    string? SuggestTargetDrive();
}
