using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Services;
using WinButler.Services.Mft;

namespace WinButler.ViewModels;

/// <summary>
/// The Disk Usage page: a WizTree-style scanner. Reads a volume's MFT (or walks a non-NTFS
/// target) via <see cref="DiskScanService"/> and presents the result as a flat, virtualized
/// list of expandable rows plus a treemap. Selecting a row re-roots the treemap on that node.
/// </summary>
public partial class DiskScannerPageViewModel : ViewModelBase
{
    private readonly DiskScanService _service;
    private readonly DiskIndexService _diskIndex;

    public ObservableCollection<ScanDrive> Drives { get; } = new();

    /// <summary>The currently-visible rows (only expanded branches are materialized).</summary>
    public ObservableCollection<DiskRowViewModel> Rows { get; } = new();

    public string[] SortModes { get; } = { "Size", "Allocated", "Name" };

    private DiskNode? _root;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private ScanDrive? _selectedDrive;

    /// <summary>A specific folder to scan instead of the whole drive (set by "Choose folder…").</summary>
    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Pick a drive or folder and click Scan.";

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private string _selectedSort = "Size";

    [ObservableProperty]
    private DiskRowViewModel? _selectedRow;

    /// <summary>The node the treemap should render — the grid-selected node, or the scan root.</summary>
    [ObservableProperty]
    private DiskNode? _selectedNode;

    public DiskScannerPageViewModel(DiskScanService service, DiskIndexService diskIndex)
    {
        _service = service;
        _diskIndex = diskIndex;
        foreach (var d in _service.GetScannableDrives())
            Drives.Add(d);
        SelectedDrive = Drives.FirstOrDefault(d => d.Letter == 'C') ?? Drives.FirstOrDefault();
    }

    private bool CanScan() => !IsBusy && (SelectedFolder is not null || SelectedDrive is not null);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        string? target = SelectedFolder ?? SelectedDrive?.RootPath;
        if (target is null)
        {
            StatusText = "Nothing to scan — pick a drive or folder.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(s => StatusText = s);
        try
        {
            // Scanning a whole drive root reuses the shared volume index (built once, shared with
            // Clean/Redirect/Dev Junk) rather than re-reading the MFT. A picked folder scans directly.
            if (SelectedFolder is null && SelectedDrive is not null)
                _root = (await _diskIndex.EnsureBuiltAsync(SelectedDrive.Letter, progress, CancellationToken.None)).Root;
            else
                _root = await _service.ScanAsync(target, progress, CancellationToken.None);

            Sort(_root, SelectedSort);
            ShowRoot();
            SelectedNode = _root;

            SummaryText =
                $"{_root.FullPath}  —  {SizeFormatter.Format(_root.SizeBytes)} in " +
                $"{_root.FileCount:N0} files, {_root.FolderCount:N0} folders";
            StatusText = "Scan complete. Click a folder's arrow to drill in.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Rebuilds the visible list as the root row, expanded one level.</summary>
    private void ShowRoot()
    {
        Rows.Clear();
        if (_root is null)
            return;

        var rootRow = new DiskRowViewModel(_root, depth: 0, Toggle);
        Rows.Add(rootRow);
        Toggle(rootRow); // expand the root so its top-level children are visible immediately
    }

    /// <summary>Expands or collapses a row by splicing its children into/out of <see cref="Rows"/>.</summary>
    private void Toggle(DiskRowViewModel row)
    {
        int index = Rows.IndexOf(row);
        if (index < 0)
            return;

        if (row.IsExpanded)
        {
            // Collapse: drop the contiguous block of deeper rows that followed this one.
            int next = index + 1;
            while (next < Rows.Count && Rows[next].Depth > row.Depth)
                Rows.RemoveAt(next);
            row.IsExpanded = false;
        }
        else
        {
            int insertAt = index + 1;
            foreach (var child in row.Node.Children)
                Rows.Insert(insertAt++, new DiskRowViewModel(child, row.Depth + 1, Toggle));
            row.IsExpanded = true;
        }
    }

    partial void OnSelectedRowChanged(DiskRowViewModel? value)
    {
        if (value is not null)
            SelectedNode = value.Node;
    }

    partial void OnSelectedSortChanged(string value)
    {
        if (_root is null)
            return;
        Sort(_root, value);
        ShowRoot();
        SelectedNode = _root;
    }

    /// <summary>Recursively reorders every node's children by the chosen key.</summary>
    private static void Sort(DiskNode node, string mode)
    {
        Comparison<DiskNode> cmp = mode switch
        {
            "Name" => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            "Allocated" => (a, b) => b.AllocBytes.CompareTo(a.AllocBytes),
            _ => (a, b) => b.SizeBytes.CompareTo(a.SizeBytes),
        };

        // Iterative post-order-free traversal: sort this node, then queue children.
        var stack = new Stack<DiskNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Children.Count > 1)
                n.Children.Sort(cmp);
            foreach (var c in n.Children)
                if (c.HasChildren)
                    stack.Push(c);
        }
    }
}
