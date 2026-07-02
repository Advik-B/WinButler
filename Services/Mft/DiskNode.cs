using System;
using System.Collections.Generic;

namespace WinButler.Services.Mft;

/// <summary>
/// One node (file or folder) in a scanned tree. Produced identically by the fast
/// <see cref="MftReader"/>/<see cref="MftTreeBuilder"/> path and by the
/// <see cref="RecursiveWalkScanner"/> fallback, so the UI never has to care which
/// engine ran. Folder <see cref="SizeBytes"/>/<see cref="AllocBytes"/> are the
/// aggregate of all descendants; for files they are the file's own size.
///
/// A whole-drive tree holds millions of file leaves, so leaves deliberately carry no
/// child list (<see cref="Children"/> is a shared empty view until the first
/// <see cref="AddChild"/>) and no stored path — a file's <see cref="FullPath"/> is
/// computed from its <see cref="Parent"/> on demand.
/// </summary>
public sealed class DiskNode
{
    public required string Name { get; set; }

    /// <summary>Owning directory, set for file leaves so <see cref="FullPath"/> can be
    /// computed on demand instead of retaining millions of path strings.</summary>
    public DiskNode? Parent { get; set; }

    private string? _fullPath;

    /// <summary>Stored for directories (the <see cref="DriveIndex"/> keys them); computed
    /// from the <see cref="Parent"/> chain for files.</summary>
    public string FullPath
    {
        get
        {
            if (_fullPath is not null)
                return _fullPath;
            if (Parent is null)
                return Name;
            var prefix = Parent.FullPath;
            return prefix.EndsWith('\\') ? prefix + Name : prefix + '\\' + Name;
        }
        set => _fullPath = value;
    }

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

    private List<DiskNode>? _children;

    /// <summary>
    /// Child nodes, largest-first once the tree is finalized. A read-only view: mutate only
    /// through <see cref="AddChild"/>/<see cref="SortChildren"/> during the build pass;
    /// treat as immutable afterwards (the tree is shared via <see cref="DriveIndex"/>).
    /// </summary>
    public IReadOnlyList<DiskNode> Children => _children ?? (IReadOnlyList<DiskNode>)Array.Empty<DiskNode>();

    public bool HasChildren => _children is { Count: > 0 };

    public void AddChild(DiskNode child) => (_children ??= new()).Add(child);

    /// <summary>Build-pass helper; a no-op on childless nodes (no list is materialized).</summary>
    public void SortChildren(Comparison<DiskNode> comparison) => _children?.Sort(comparison);
}
