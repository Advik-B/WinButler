using System;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Services;
using WinButler.Services.Mft;

namespace WinButler.ViewModels;

/// <summary>
/// One visible row in the WizTree-style list. The grid is rendered as a flat, virtualized list
/// of only the currently-visible nodes (collapsed subtrees aren't materialized), with the tree
/// shape conveyed by <see cref="Depth"/>-based indentation and an expander glyph in the Name
/// cell — the same approach WizTree uses, and it keeps the numeric columns perfectly aligned
/// regardless of nesting depth.
/// </summary>
public sealed partial class DiskRowViewModel : ObservableObject
{
    private readonly Action<DiskRowViewModel> _onToggle;

    public DiskRowViewModel(DiskNode node, int depth, Action<DiskRowViewModel> onToggle)
    {
        Node = node;
        Depth = depth;
        _onToggle = onToggle;
    }

    public DiskNode Node { get; }
    public int Depth { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasChildren => Node.HasChildren;

    public string Name => Node.Name;
    public string FullPath => Node.FullPath;
    public bool IsDirectory => Node.IsDirectory;

    public string SizeText => SizeFormatter.Format(Node.SizeBytes);
    public string AllocText => SizeFormatter.Format(Node.AllocBytes);

    public double PercentValue => Node.PercentOfParent * 100.0;
    public string PercentText => Node.PercentOfParent.ToString("P1");

    public string FilesText => Node.IsDirectory ? Node.FileCount.ToString("N0") : "";
    public string FoldersText => Node.IsDirectory ? Node.FolderCount.ToString("N0") : "";
    public string ModifiedText => Node.Modified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";

    /// <summary>Left indent for the Name cell: 18px per tree level.</summary>
    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    /// <summary>Disclosure triangle: down when expanded, right when collapsed, blank for files.</summary>
    public string ExpanderGlyph => !HasChildren ? "" : (IsExpanded ? "▾" : "▸");

    public ICommand ToggleCommand => new RelayCommand(() => _onToggle(this));

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpanderGlyph));
}
