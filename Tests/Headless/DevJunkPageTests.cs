using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Logic tests for the Dev Junk page's SELECT ALL / CLEAR commands. Groups are added directly
/// (never through <c>ScanCommand</c>, which forces a real MFT read), so nothing touches the disk.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DevJunkPageTests : MessengerIsolatedTest
{
    private static DevJunkPageViewModel NewPage()
    {
        var settings = new AppSettings();
        var index = new DiskIndexService(new FakeDiskScanService());
        var redirect = new RedirectPageViewModel(settings, new FakeRedirectionService(), index);
        return new DevJunkPageViewModel(new DevJunkAggregator(), settings, new FakeCleaner(), redirect, _ => { }, index);
    }

    private static DevToolGroupViewModel Group(string name, long reclaimable, bool locked = false) =>
        new(new DevToolGroup
        {
            SourcePath = $@"C:\Users\me\{name}",
            DisplayName = name,
            Description = "test",
            Category = "Build tools",
            TargetName = name,
            OnDiskBytes = reclaimable,
            ReclaimableBytes = reclaimable,
            IsLocked = locked,
        });

    [AvaloniaFact]
    public void First_visit_scan_button_is_enabled()
    {
        // The first-visit empty state shows a SCAN button bound to ScanCommand — it must be
        // invokable before any scan has run.
        var vm = NewPage();

        Assert.False(vm.HasScanned);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SelectAll_ticks_only_selectable_groups_and_Clear_unticks_all()
    {
        var vm = NewPage();
        var reclaimable = Group("gradle", 100);
        var locked = Group("dotfiles", 100, locked: true); // protected → not selectable
        vm.Groups.Add(reclaimable);
        vm.Groups.Add(locked);

        vm.SelectAllCommand.Execute(null);
        Assert.True(reclaimable.IsSelected);   // selectable → ticked
        Assert.False(locked.IsSelected);       // locked → left alone

        vm.ClearCommand.Execute(null);
        Assert.False(reclaimable.IsSelected);
        Assert.False(locked.IsSelected);
    }
}
