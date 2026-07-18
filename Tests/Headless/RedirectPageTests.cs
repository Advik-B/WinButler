using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Tests the inline "undo" on an already-redirected candidate row — a junction that may have no
/// ledger record. Uses <see cref="FakeRedirectionService"/> (its UndoAsync reports "fake").
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RedirectPageTests : MessengerIsolatedTest
{
    private static DiskIndexService FakeIndex() => new(new FakeDiskScanService());

    private static RedirectCandidateViewModel Redirected() =>
        new(new RedirectCandidate
        {
            SourcePath = @"C:\Users\me\.gradle",
            DisplayName = ".gradle",
            Description = "Gradle caches",
            TargetName = ".gradle",
            IsAlreadyRedirected = true,
            ExistingTarget = @"D:\_redirected\.gradle",
        });

    [AvaloniaFact]
    public async Task UndoCandidate_on_an_already_redirected_row_routes_through_the_service()
    {
        var vm = new RedirectPageViewModel(new AppSettings(), new FakeRedirectionService(), FakeIndex());
        var candidate = Redirected();
        vm.Candidates.Add(candidate);

        await vm.UndoCandidateCommand.ExecuteAsync(candidate);

        // Reached FakeRedirectionService.UndoAsync (dry-run default → no confirm, no rescan).
        Assert.Equal("fake", vm.StatusText);
    }

    [AvaloniaFact]
    public void First_visit_scan_button_is_enabled()
    {
        // The first-visit empty state shows a SCAN button bound to ScanCommand — it must be
        // invokable before any scan has run.
        var vm = new RedirectPageViewModel(new AppSettings(), new FakeRedirectionService(), FakeIndex());

        Assert.False(vm.HasScanned);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void UndoCandidate_is_a_no_op_for_a_folder_that_is_not_redirected()
    {
        var vm = new RedirectPageViewModel(new AppSettings(), new FakeRedirectionService(), FakeIndex());
        var normal = new RedirectCandidateViewModel(new RedirectCandidate
        {
            SourcePath = @"C:\Users\me\.gradle",
            DisplayName = ".gradle",
            Description = "Gradle caches",
            TargetName = ".gradle",
            IsAlreadyRedirected = false,
        });
        var before = vm.StatusText;

        vm.UndoCandidateCommand.Execute(normal); // guard returns early (no ExistingTarget)

        Assert.Equal(before, vm.StatusText);
    }
}
