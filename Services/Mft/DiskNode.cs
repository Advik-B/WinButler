using System;
using System.Collections.Generic;

namespace WinButler.Services.Mft;

/// <summary>
/// One node (file or folder) in a scanned tree. Produced identically by the fast
/// <see cref="MftReader"/>/<see cref="MftTreeBuilder"/> path and by the
/// <see cref="RecursiveWalkScanner"/> fallback, so the UI never has to care which
/// engine ran. Folder <see cref="SizeBytes"/>/<see cref="AllocBytes"/> are the
/// aggregate of all descendants; for files they are the file's own size.
/// </summary>
public sealed class DiskNode
{
    public required string Name { get; set; }
    public required string FullPath { get; set; }
    public required bool IsDirectory { get; init; }

    /// <summary>Real ($DATA) bytes — what WizTree shows in the "Size" column.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Allocated bytes (cluster-rounded) — WizTree's "Allocated" column.</summary>
    public long AllocBytes { get; set; }

    /// <summary>Count of descendant files (0 for a leaf file).</summary>
    public long FileCount { get; set; }

    /// <summary>Count of descendant folders.</summary>
    public long FolderCount { get; set; }

    public DateTime? Modified { get; set; }

    /// <summary>This node's <see cref="SizeBytes"/> as a fraction (0..1) of its parent's.</summary>
    public double PercentOfParent { get; set; }

    /// <summary>
    /// Child nodes, largest-first once the tree is finalized. Mutable during the build
    /// pass; treat as read-only afterwards.
    /// </summary>
    public List<DiskNode> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;
}
