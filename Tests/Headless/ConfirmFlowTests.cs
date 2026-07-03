using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Mft;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// The confirm-before-destruction flow (P4.1). Real (dry-run-off) cleans prompt; dry-run
/// never does; declining deletes nothing; an unset delegate auto-confirms so pre-existing
/// tests that drive the commands directly stay valid. Uses a fake index so the post-clean
/// rescan never touches a real disk.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ConfirmFlowTests : MessengerIsolatedTest
{
    private static CleanPageViewModel NewPage(AppSettings settings, ICleaner cleaner) =>
        new(settings, Fakes.CleanScanners(), cleaner, new DiskIndexService(new FakeDiskScanService()));

    [AvaloniaFact]
    public async Task Real_clean_prompts_and_declining_deletes_nothing()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings { IsDryRun = false }, cleaner);
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100) }); // Safe → preselected

        int prompts = 0;
        vm.ConfirmInteraction = _ => { prompts++; return Task.FromResult(false); };

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts);
        Assert.Empty(cleaner.Calls);                 // declined → nothing deleted
        Assert.Contains("Cancelled", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Real_clean_proceeds_when_confirmed_with_a_permanent_recycle_breakdown()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings { IsDryRun = false }, cleaner);
        // One Safe (→ permanent) and one Caution (→ Recycle Bin), both selected.
        var caution = Fakes.Target("b", 200, RiskLevel.Caution);
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100), caution });
        vm.CacheCategory.Items.First(i => i.DisplayName == "b").IsSelected = true;

        ConfirmRequest? seen = null;
        vm.ConfirmInteraction = req => { seen = req; return Task.FromResult(true); };

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.Equal(2, seen!.Count);
        Assert.Contains("permanently", seen.Detail);
        Assert.Contains("Recycle Bin", seen.Detail);
        Assert.Equal(2, cleaner.Calls.Count);
    }

    [AvaloniaFact]
    public async Task Dry_run_never_prompts()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings(), cleaner); // dry-run defaults on
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100) });

        int prompts = 0;
        vm.ConfirmInteraction = _ => { prompts++; return Task.FromResult(true); };

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, prompts);
        Assert.Single(cleaner.Calls);   // dry-run still simulates the clean
    }

    [AvaloniaFact]
    public async Task Unset_delegate_auto_confirms_a_real_clean()
    {
        var cleaner = new FakeCleaner();
        var vm = NewPage(new AppSettings { IsDryRun = false }, cleaner);
        vm.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100) });
        // ConfirmInteraction deliberately left null

        await vm.CleanSelectedCommand.ExecuteAsync(null);

        Assert.Single(cleaner.Calls); // proceeds without prompting
    }

    [AvaloniaFact]
    public async Task Clean_all_prompts_exactly_once_across_both_pages()
    {
        var shell = new MainWindowViewModel();
        shell.Settings.IsDryRun = false;
        shell.CleanPage.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100) });

        int prompts = 0;
        shell.DashboardPage.ConfirmInteraction = _ => { prompts++; return Task.FromResult(false); };

        await shell.DashboardPage.CleanAllCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts); // one aggregated confirm, not one per page
    }
}
