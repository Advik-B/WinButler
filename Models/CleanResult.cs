namespace WinButler.Models;

/// <summary>Outcome of attempting to clean a single <see cref="CleanupTarget"/>.</summary>
public sealed class CleanResult
{
    public required CleanupTarget Target { get; init; }

    /// <summary>True when the target was (or, in dry-run, would be) removed successfully.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>True when this was a simulation and nothing was actually touched.</summary>
    public required bool WasDryRun { get; init; }

    /// <summary>Bytes reclaimed (or projected to be reclaimed in dry-run).</summary>
    public long BytesReclaimed { get; init; }

    /// <summary>Populated when <see cref="Succeeded"/> is false (e.g. "skipped: in use").</summary>
    public string? Error { get; init; }
}
