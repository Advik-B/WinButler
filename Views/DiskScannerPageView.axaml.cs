using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WinButler.Services.Mft;
using WinButler.ViewModels;

namespace WinButler.Views;

public partial class DiskScannerPageView : UserControl
{
    public DiskScannerPageView()
    {
        InitializeComponent();
        Treemap.NodeInvoked += OnTreemapNodeInvoked;
    }

    /// <summary>Clicking a treemap rectangle re-roots the treemap (and selection) on that node.</summary>
    private void OnTreemapNodeInvoked(object? sender, DiskNode node)
    {
        if (DataContext is DiskScannerPageViewModel vm)
            vm.SelectedNode = node;
    }

    /// <summary>Lets the user pick any folder to scan instead of a whole drive, then scans it.</summary>
    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiskScannerPageViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan",
            AllowMultiple = false,
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        vm.SelectedFolder = path;
        if (vm.ScanCommand.CanExecute(null))
            vm.ScanCommand.Execute(null);
    }
}
