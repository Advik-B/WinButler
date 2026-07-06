using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Privacy;
using WinButler.ViewModels;
using WinButler.Views;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Logic tests for <see cref="SystemToolsPageViewModel"/> using a fake runner so nothing is really
/// executed: dry-run must print without launching, live destructive actions must confirm first, and
/// read-only actions run even while simulating.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SystemToolsPageTests : MessengerIsolatedTest
{
    /// <summary>Records what it was asked to run; optionally blocks until released (busy-gating test).</summary>
    private sealed class FakeRunner : SystemActionRunner
    {
        public List<SystemCommand> Ran { get; } = new();
        public TaskCompletionSource? Gate { get; set; }

        public override async Task<int> RunAsync(
            IReadOnlyList<SystemCommand> steps, IProgress<string> output, CancellationToken ct = default)
        {
            Ran.AddRange(steps);
            if (Gate is not null)
                await Gate.Task;
            return 0;
        }
    }

    // A registry editor that records nothing exists — privacy ops are exercised in PrivacyCleanerTests.
    private sealed class EmptyRegistry : IRegistryEditor
    {
        public IReadOnlyList<string> GetValueNames(string subKey) => System.Array.Empty<string>();
        public void DeleteValue(string subKey, string valueName) { }
    }

    private static SystemToolsPageViewModel NewVm(bool dryRun, SystemActionRunner runner) =>
        new(new AppSettings { IsDryRun = dryRun }, runner, new PrivacyCleaner(new EmptyRegistry()));

    private static SystemAction Analyze(SystemToolsPageViewModel vm) =>
        vm.Actions.Single(a => a.Id == "analyze-store");

    private static SystemAction ComponentCleanup(SystemToolsPageViewModel vm) =>
        vm.Actions.Single(a => a.Id == "component-cleanup");

    [AvaloniaFact]
    public async Task Dry_run_prints_the_commands_and_launches_nothing()
    {
        var runner = new FakeRunner();
        var vm = NewVm(true, runner);

        await vm.RunActionCommand.ExecuteAsync(ComponentCleanup(vm));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(runner.Ran); // nothing executed
        Assert.Contains(vm.Output, l => l.Contains("DRY RUN"));
        Assert.Contains(vm.Output, l => l.Contains("StartComponentCleanup"));
    }

    [AvaloniaFact]
    public async Task Read_only_action_runs_even_in_dry_run_and_never_confirms()
    {
        var runner = new FakeRunner();
        var vm = NewVm(true, runner);
        var confirmed = false;
        vm.ConfirmInteraction = _ => { confirmed = true; return Task.FromResult(true); };

        await vm.RunActionCommand.ExecuteAsync(Analyze(vm));
        Dispatcher.UIThread.RunJobs();

        Assert.False(confirmed);                                  // no prompt for a read-only action
        Assert.Contains(runner.Ran, c => c.Arguments.Contains("AnalyzeComponentStore"));
    }

    [AvaloniaFact]
    public async Task Live_destructive_action_declined_at_confirm_does_not_run()
    {
        var runner = new FakeRunner();
        var vm = NewVm(false, runner);
        vm.ConfirmInteraction = _ => Task.FromResult(false); // user says no

        await vm.RunActionCommand.ExecuteAsync(ComponentCleanup(vm));

        Assert.Empty(runner.Ran);
        Assert.Equal("Cancelled.", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Live_destructive_action_confirmed_runs_the_commands()
    {
        var runner = new FakeRunner();
        var vm = NewVm(false, runner);
        vm.ConfirmInteraction = _ => Task.FromResult(true);

        await vm.RunActionCommand.ExecuteAsync(ComponentCleanup(vm));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(runner.Ran, c => c.Arguments.Contains("StartComponentCleanup"));
    }

    [AvaloniaFact]
    public void Other_actions_are_blocked_while_one_is_running()
    {
        var runner = new FakeRunner { Gate = new TaskCompletionSource() };
        var vm = NewVm(false, runner);
        vm.ConfirmInteraction = _ => Task.FromResult(true);

        var running = vm.RunActionCommand.ExecuteAsync(ComponentCleanup(vm));
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsBusy);
        Assert.False(vm.RunActionCommand.CanExecute(Analyze(vm))); // can't start another mid-run

        runner.Gate!.SetResult();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Records each batch's first command; can simulate a cancel landing on the stop step.</summary>
    private sealed class WuRunner : SystemActionRunner
    {
        public List<string> Calls { get; } = new();
        public bool CancelOnStop { get; set; }

        public override Task<int> RunAsync(
            IReadOnlyList<SystemCommand> steps, IProgress<string> output, CancellationToken ct = default)
        {
            var first = steps[0].Display;
            Calls.Add(first);
            if (CancelOnStop && first.Contains("stop"))
                throw new OperationCanceledException(); // cancel lands mid-stop
            return Task.FromResult(0);
        }
    }

    [AvaloniaFact]
    public async Task Windows_update_flush_restarts_services_even_if_cancelled_during_stop()
    {
        var runner = new WuRunner { CancelOnStop = true };
        var vm = new SystemToolsPageViewModel(new AppSettings { IsDryRun = false }, runner, new PrivacyCleaner(new EmptyRegistry()));
        vm.ConfirmInteraction = _ => Task.FromResult(true);
        var wu = vm.Actions.Single(a => a.Id == "wu-cache");

        await vm.RunActionCommand.ExecuteAsync(wu);
        Dispatcher.UIThread.RunJobs();

        // The stop threw (cancel), but the services MUST still be restarted — never left disabled.
        Assert.Contains(runner.Calls, c => c.Contains("stop wuauserv"));
        Assert.Contains(runner.Calls, c => c.Contains("start wuauserv"));
    }

    [AvaloniaFact]
    public void Advanced_actions_are_separated_from_the_regular_ones()
    {
        var vm = NewVm(true, new FakeRunner());

        Assert.Contains(vm.AdvancedActions, a => a.Id == "wmi-reset");
        Assert.Contains(vm.AdvancedActions, a => a.Id == "event-log-clear");
        Assert.All(vm.AdvancedActions, a => Assert.True(a.IsAdvanced));
        Assert.DoesNotContain(vm.Actions, a => a.IsAdvanced);
    }

    /// <summary>
    /// Renders the actual view (not just the VM) to prove the per-item RUN button's
    /// <c>$parent[ItemsControl]…RunActionCommand</c> binding resolves — a wiring shape no other page
    /// uses, which VM-only tests can't see. A broken cast would leave Command null and the button
    /// silently dead. This is the one view-rendering test; the rest of the suite stays VM-only.
    /// </summary>
    [AvaloniaFact]
    public void Run_button_binding_resolves_to_the_command_and_invokes_it()
    {
        var runner = new FakeRunner();
        var vm = NewVm(true, runner); // dry-run → invoking just prints, launches nothing
        var window = new Window { Content = new SystemToolsPageView { DataContext = vm }, Width = 900, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var runButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => (b.Content as string) == "RUN").ToList();

        Assert.NotEmpty(runButtons);                       // the action rows rendered
        Assert.All(runButtons, b => Assert.NotNull(b.Command));       // the $parent binding resolved
        Assert.All(runButtons, b => Assert.IsType<SystemAction>(b.CommandParameter));

        // Click a known dry-run-printing action's button and confirm it actually drove the VM.
        var cleanupButton = runButtons.First(b => (b.CommandParameter as SystemAction)?.Id == "component-cleanup");
        cleanupButton.Command!.Execute(cleanupButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.Output, l => l.Contains("DRY RUN"));
    }
}
