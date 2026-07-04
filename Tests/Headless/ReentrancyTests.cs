using Avalonia.Headless.XUnit;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Gating tests for the commands that used to be double-fireable: RE-SCAN, the Dashboard's
/// CLEAN ALL, and Redirect's Undo. Each must report CanExecute=false while the work it would
/// trigger (or overlap) is already in flight.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ReentrancyTests : MessengerIsolatedTest
{
    private static DiskIndexService FakeIndex() => new(new FakeDiskScanService());

    [AvaloniaFact]
    public void CleanAll_is_disabled_while_either_child_page_is_busy()
    {
        var settings = new AppSettings();
        var index = FakeIndex();
        var clean = new CleanPageViewModel(settings, Fakes.CleanScanners(), new FakeCleaner(), index);
        var redirect = new RedirectPageViewModel(settings, new FakeRedirectionService(), index);
        var devJunk = new DevJunkPageViewModel(
            new DevJunkAggregator(), settings, new FakeCleaner(), redirect, _ => { }, index);
        var dash = new DashboardPageViewModel(clean, redirect, devJunk, _ => { });

        Assert.True(dash.CleanAllCommand.CanExecute(null));

        clean.IsBusy = true;
        Assert.False(dash.CleanAllCommand.CanExecute(null));

        clean.IsBusy = false;
        devJunk.IsBusy = true;
        Assert.False(dash.CleanAllCommand.CanExecute(null));

        devJunk.IsBusy = false;
        Assert.True(dash.CleanAllCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Undo_is_disabled_while_the_redirect_page_is_busy()
    {
        var vm = new RedirectPageViewModel(new AppSettings(), new FakeRedirectionService(), FakeIndex());

        Assert.True(vm.UndoCommand.CanExecute(null));

        vm.IsBusy = true;
        Assert.False(vm.UndoCommand.CanExecute(null));

        vm.IsBusy = false;
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void RescanAll_is_disabled_while_a_rescan_is_running()
    {
        var shell = new MainWindowViewModel();

        Assert.True(shell.RescanAllCommand.CanExecute(null));

        shell.IsRescanning = true;
        Assert.False(shell.RescanAllCommand.CanExecute(null));

        shell.IsRescanning = false;
        Assert.True(shell.RescanAllCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Choosing_a_drive_resets_a_previously_picked_folder()
    {
        var vm = new DiskScannerPageViewModel(new FakeDiskScanService(), FakeIndex());
        vm.SelectedFolder = @"C:\some\deep\folder";

        vm.SelectedDrive = new ScanDrive('Z', "NTFS", true, "Z: — test");

        Assert.Null(vm.SelectedFolder);
    }
}
