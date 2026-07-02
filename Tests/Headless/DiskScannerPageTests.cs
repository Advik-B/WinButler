using System.Linq;
using Avalonia.Headless.XUnit;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Disk Explorer row logic against a hand-built tree (via the SetRootForTest seam): sort modes
/// order the VISIBLE rows without ever re-ordering the shared tree's child lists, and
/// expand/collapse splices the flat row list correctly.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiskScannerPageTests : MessengerIsolatedTest
{
    private static DiskNode File(string name, long size) => new()
    {
        Name = name,
        FullPath = @"C:\" + name,
        IsDirectory = false,
        SizeBytes = size,
        AllocBytes = size,
    };

    private static DiskNode Tree()
    {
        var root = new DiskNode { Name = @"C:\", FullPath = @"C:\", IsDirectory = true };
        var dirB = new DiskNode { Name = "bravo", FullPath = @"C:\bravo", IsDirectory = true, SizeBytes = 200, AllocBytes = 90 };
        dirB.Children.Add(File("inner.bin", 200));
        // Deliberately NOT size-ordered, so tests can detect any in-place re-sorting.
        root.Children.Add(File("alpha.bin", 100));
        root.Children.Add(dirB);
        root.Children.Add(File("charlie.bin", 50));
        root.SizeBytes = 350;
        return root;
    }

    private static DiskScannerPageViewModel NewPage() =>
        new(new FakeDiskScanService(), new DiskIndexService(new FakeDiskScanService()));

    [AvaloniaFact]
    public void Rows_are_sorted_by_size_without_mutating_the_shared_tree()
    {
        var vm = NewPage();
        var root = Tree();
        vm.SetRootForTest(root);

        // Root row + its children, largest first.
        Assert.Equal(new[] { @"C:\", "bravo", "alpha.bin", "charlie.bin" }, vm.Rows.Select(r => r.Name));

        // The tree itself keeps its original child order — it is the shared index cache.
        Assert.Equal(new[] { "alpha.bin", "bravo", "charlie.bin" }, root.Children.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Switching_sort_reorders_rows_only()
    {
        var vm = NewPage();
        var root = Tree();
        vm.SetRootForTest(root);

        vm.SelectedSort = "Name";

        Assert.Equal(new[] { @"C:\", "alpha.bin", "bravo", "charlie.bin" }, vm.Rows.Select(r => r.Name));
        Assert.Equal(new[] { "alpha.bin", "bravo", "charlie.bin" }, root.Children.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Expand_and_collapse_splice_the_flat_row_list()
    {
        var vm = NewPage();
        vm.SetRootForTest(Tree());

        var bravo = vm.Rows.Single(r => r.Name == "bravo");
        bravo.ToggleCommand.Execute(null);

        Assert.True(bravo.IsExpanded);
        int bravoIndex = vm.Rows.IndexOf(bravo);
        Assert.Equal("inner.bin", vm.Rows[bravoIndex + 1].Name);          // spliced in directly below
        Assert.Equal(bravo.Depth + 1, vm.Rows[bravoIndex + 1].Depth);

        bravo.ToggleCommand.Execute(null);

        Assert.False(bravo.IsExpanded);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "inner.bin");        // spliced back out
        Assert.Equal(4, vm.Rows.Count);
    }
}
