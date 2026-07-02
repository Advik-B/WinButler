using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Cancel-button flow: cancelling a running scan must read as a normal outcome ("Cancelled."),
/// release the page, and re-enable its commands — driven with a scanner that hangs until its
/// token fires, so nothing here races real I/O.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CancellationTests : MessengerIsolatedTest
{
    [AvaloniaFact]
    public async Task Cancel_during_a_scan_reports_cancelled_and_releases_the_page()
    {
        var scanners = new IScanner[]
        {
            new FakeScanner(CleanupCategory.ElectronLeftover, "Electron Leftovers"),
            new FakeScanner(CleanupCategory.Temp, "Temp Files"),
            new HangingScanner(CleanupCategory.Cache, "Cache Sweep"),
        };
        var vm = new CleanPageViewModel(
            new AppSettings(), scanners, new FakeCleaner(),
            new DiskIndexService(new FakeDiskScanService()));

        var run = vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.False(vm.ScanCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        await run;

        Assert.Equal("Cancelled.", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null)); // nothing left to cancel
    }
}
