using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Interaction/logic tests for <see cref="CleanPageViewModel"/>. Categories are populated directly
/// via <see cref="CategoryViewModel.SetItems"/> (never through <c>ScanCommand</c>, which forces a
/// real MFT read), so nothing here touches the disk or needs admin.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CleanPageTests : MessengerIsolatedTest
{
    private static CleanPageViewModel NewPage(AppSettings settings, ICleaner cleaner) =>
        new(settings, Fakes.CleanScanners(), cleaner, new DiskIndexService(new DiskScanService()));

    [AvaloniaFact]
    public void Safe_items_are_preselected_and_totals_reflect_selection()
    {
        var vm = NewPage(new AppSettings(), new FakeCleaner());
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100), Fakes.Target("b", 200) });

        // Both Safe → pre-selected.
        Assert.Equal(300, vm.SelectedBytes);

        // Items are ordered largest-first, so First() is the 200-byte target; deselecting it leaves 100.
        vm.CacheCategory.Items.First().IsSelected = false;
        Assert.Equal(100, vm.SelectedBytes);
        Assert.Contains("selected", vm.SelectedSummary);
    }

    [AvaloniaFact]
    public void Caution_items_are_not_preselected()
    {
        var vm = NewPage(new AppSettings(), new FakeCleaner());
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("risky", 500, RiskLevel.Caution) });

        Assert.Equal(0, vm.SelectedBytes);
    }

    [AvaloniaFact]
    public void SelectAll_and_SelectNone_flip_every_item()
    {
        var vm = NewPage(new AppSettings(), new FakeCleaner());
        var cat = vm.CacheCategory!;
        cat.SetItems(new[] { Fakes.Target("a", 100, RiskLevel.Caution), Fakes.Target("b", 200, RiskLevel.Caution) });

        cat.SelectAllCommand.Execute(null);
        Assert.Equal(300, vm.SelectedBytes);

        cat.SelectNoneCommand.Execute(null);
        Assert.Equal(0, vm.SelectedBytes);
    }

    [AvaloniaFact]
    public void IsBusy_disables_scan_and_clean_commands()
    {
        var vm = NewPage(new AppSettings(), new FakeCleaner());

        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.True(vm.CleanSelectedCommand.CanExecute(null));

        vm.IsBusy = true;

        Assert.False(vm.ScanCommand.CanExecute(null));
        Assert.False(vm.CleanSelectedCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Dry_run_clean_deletes_nothing_and_reports_a_dry_run()
    {
        var settings = new AppSettings(); // IsDryRun defaults to true
        var cleaner = new FakeCleaner();
        var vm = NewPage(settings, cleaner);
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100), Fakes.Target("b", 200) });

        var received = new List<CleanupCompletedMessage>();
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<CleanupCompletedMessage>(recipient, (_, m) => received.Add(m));

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.True(settings.IsDryRun);
        Assert.All(cleaner.Calls, c => Assert.True(c.DryRun));   // every call was a simulation
        Assert.Equal(2, cleaner.Calls.Count);
        Assert.Contains("DRY RUN", vm.StatusText);

        // The run is still broadcast (tagged as a dry run) for the Session Activity feed.
        Assert.Single(received);
        Assert.True(received[0].DryRun);
        Assert.Equal(CleanupAction.Clean, received[0].Action);

        GC.KeepAlive(recipient);
    }

    [AvaloniaFact]
    public async Task Clean_with_nothing_selected_is_a_no_op()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings(), cleaner);
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100, RiskLevel.Caution) }); // not pre-selected

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.Empty(cleaner.Calls);
        Assert.Equal("Nothing selected.", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task CleanSelected_scoped_to_a_category_ignores_other_categories()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings(), cleaner);
        // Cache has two pre-selected Safe items (the old cross-category leak source)…
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("c1", 100), Fakes.Target("c2", 200) });
        // …while the Temp page shows just one selected item.
        vm.TempCategory!.SetItems(new[] { Fakes.Target("t1", 50, category: CleanupCategory.Temp) });

        // Cleaning from the Temp page passes its own category → only the 1 Temp item is deleted,
        // NOT Cache's 2 pre-selected items. (Passing null still cleans everything, for CLEAN ALL.)
        await vm.CleanSelectedCommand.ExecuteAsync(vm.TempCategory);

        Assert.Single(cleaner.Calls);
        Assert.Equal(@"C:\fake\t1", cleaner.Calls[0].Target.FullPath);
    }
}
