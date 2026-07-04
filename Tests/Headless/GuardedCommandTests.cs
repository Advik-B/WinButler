using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Error-path tests for the guarded async commands: a faulting scanner/cleaner/index build must
/// land in the page's status line and release the command — never crash the process (which is
/// what an unguarded AsyncRelayCommand fault does on the UI SynchronizationContext).
/// Uses <see cref="FakeDiskScanService"/> so index-building commands run without a real MFT read.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class GuardedCommandTests : MessengerIsolatedTest
{
    private static DiskIndexService FakeIndex(Func<DiskNode>? result = null) =>
        new(new FakeDiskScanService(result));

    private static IScanner[] ScannersWithThrowingCache(Exception? exception = null) => new IScanner[]
    {
        new FakeScanner(CleanupCategory.ElectronLeftover, "Electron Leftovers"),
        new FakeScanner(CleanupCategory.Temp, "Temp Files"),
        new ThrowingScanner(CleanupCategory.Cache, "Cache Sweep", exception),
    };

    [AvaloniaFact]
    public async Task Scan_failure_lands_in_status_and_releases_the_command()
    {
        var vm = new CleanPageViewModel(
            new AppSettings(), ScannersWithThrowingCache(), new FakeCleaner(), FakeIndex());

        await vm.ScanCommand.ExecuteAsync(null); // must not throw

        Assert.StartsWith("Scan failed:", vm.StatusText);
        Assert.Contains("scanner exploded", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.False(vm.HasScanned);
    }

    [AvaloniaFact]
    public async Task Clean_failure_lands_in_status_and_releases_the_command()
    {
        var vm = new CleanPageViewModel(
            new AppSettings(), Fakes.CleanScanners(), new ThrowingCleaner(), FakeIndex());
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100) });

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.StartsWith("Clean failed:", vm.StatusText);
        Assert.Contains("cleaner exploded", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CleanSelectedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cancellation_reads_as_cancelled_not_as_a_failure()
    {
        var vm = new CleanPageViewModel(
            new AppSettings(), ScannersWithThrowingCache(new OperationCanceledException()),
            new FakeCleaner(), FakeIndex());

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Cancelled.", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Redirect_scan_failure_lands_in_status_and_releases_the_command()
    {
        var index = FakeIndex(() => throw new UnauthorizedAccessException("no volume handle"));
        var vm = new RedirectPageViewModel(new AppSettings(), new FakeRedirectionService(), index);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.StartsWith("Scan failed:", vm.StatusText);
        Assert.Contains("no volume handle", vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }
}
