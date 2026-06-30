using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Finds cleanup candidates for a single <see cref="CleanupCategory"/>.
/// Scanners are read-only: they never mutate the file system.
/// </summary>
public interface IScanner
{
    CleanupCategory Category { get; }

    /// <summary>Human-readable category title for the UI.</summary>
    string Title { get; }

    Task<IReadOnlyList<CleanupTarget>> ScanAsync(CancellationToken ct = default);
}
