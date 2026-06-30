using System;

namespace WinButler.Models;

/// <summary>A directory that can be relocated to another drive and left behind as a junction.</summary>
public sealed class RedirectCandidate
{
    public required string SourcePath { get; init; }

    /// <summary>Friendly label (e.g. ".gradle" or "JetBrains").</summary>
    public required string DisplayName { get; init; }

    /// <summary>What lives here (e.g. "Gradle build cache & wrappers").</summary>
    public required string Description { get; init; }

    /// <summary>Grouping label for the UI (e.g. "Build tools", "Games").</summary>
    public string Category { get; init; } = "Other";

    /// <summary>Unique destination folder name under &lt;drive&gt;:\_redirected\.</summary>
    public required string TargetName { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>True if the source path is already a junction (already redirected).</summary>
    public bool IsAlreadyRedirected { get; init; }

    /// <summary>If redirected, where it currently points.</summary>
    public string? ExistingTarget { get; init; }
}

/// <summary>A persisted record of a completed redirection, used for reliable undo.</summary>
public sealed class RedirectRecord
{
    public required string SourcePath { get; set; }
    public required string TargetPath { get; set; }
    public required string TimestampUtc { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>Result of a redirect or undo operation.</summary>
public sealed class RedirectResult
{
    public required bool Succeeded { get; init; }
    public required bool WasDryRun { get; init; }
    public required string Message { get; init; }
    public long BytesMoved { get; init; }
}
