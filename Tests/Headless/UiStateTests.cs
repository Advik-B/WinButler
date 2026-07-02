using System;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// The state flags behind the pages' list-vs-empty switches, and the shell toast that now
/// fires on every completed clean/redirect broadcast.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class UiStateTests : MessengerIsolatedTest
{
    [AvaloniaFact]
    public void Category_HasItems_flips_with_scan_results()
    {
        var shell = new MainWindowViewModel();
        var cat = shell.CleanPage.CacheCategory!;

        Assert.False(cat.HasItems);

        cat.SetItems(new[] { Fakes.Target("a", 100) });
        Assert.True(cat.HasItems);

        cat.SetItems(Array.Empty<CleanupTarget>());
        Assert.False(cat.HasItems);
    }

    [AvaloniaFact]
    public void Completed_cleanup_broadcast_raises_a_toast_on_the_shell()
    {
        var shell = new MainWindowViewModel();
        Assert.Null(shell.CurrentToast);

        WeakReferenceMessenger.Default.Send(
            new CleanupCompletedMessage(CleanupAction.Clean, 2048, 3, DryRun: true, Time: DateTime.Now));
        Dispatcher.UIThread.RunJobs(); // the toast is Posted — flush before asserting

        Assert.NotNull(shell.CurrentToast);
        Assert.Contains("Dry run", shell.CurrentToast!.Message);
        Assert.Equal(ToastKind.Dry, shell.CurrentToast.Kind);
    }

    [AvaloniaFact]
    public void Real_cleanup_broadcast_raises_an_ok_toast()
    {
        var shell = new MainWindowViewModel();

        WeakReferenceMessenger.Default.Send(
            new CleanupCompletedMessage(CleanupAction.Redirect, 4096, 1, DryRun: false, Time: DateTime.Now));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(shell.CurrentToast);
        Assert.Contains("Moved", shell.CurrentToast!.Message);
        Assert.Equal(ToastKind.Ok, shell.CurrentToast.Kind);
    }
}
