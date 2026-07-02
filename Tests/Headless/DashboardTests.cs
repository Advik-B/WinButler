using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using WinButler.Models;
using WinButler.Services;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Tests the Dashboard's aggregation and Session Activity feed. Uses a full
/// <see cref="MainWindowViewModel"/> (which wires the real services but scans nothing on
/// construction) and drives it by populating a Clean category directly.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DashboardTests : MessengerIsolatedTest
{
    [AvaloniaFact]
    public void ReclaimNow_and_cards_aggregate_from_category_items()
    {
        var shell = new MainWindowViewModel();
        shell.CleanPage.CacheCategory!.SetItems(new[] { Fakes.Target("a", 100), Fakes.Target("b", 200) });

        Assert.Equal(300, shell.DashboardPage.ReclaimNowBytes);

        // The Dashboard rebuilds its four category cards on any category change.
        var cacheCard = shell.DashboardPage.CategoryCards.FirstOrDefault(c => c.Title == "Cache Sweep");
        Assert.NotNull(cacheCard);
        Assert.Equal(SizeFormatter.Format(300), cacheCard!.SizeText);
    }

    [AvaloniaFact]
    public void Cleanup_message_prepends_a_session_activity_entry()
    {
        var shell = new MainWindowViewModel();
        var dash = shell.DashboardPage;

        Assert.False(dash.HasActivity);
        Assert.Empty(dash.SessionActivity);

        WeakReferenceMessenger.Default.Send(
            new CleanupCompletedMessage(CleanupAction.Clean, 4096, 3, DryRun: true, Time: DateTime.Now));

        // OnCleanupCompleted marshals through Dispatcher.UIThread.Post — flush it before asserting.
        Dispatcher.UIThread.RunJobs();

        Assert.True(dash.HasActivity);
        Assert.Single(dash.SessionActivity);
        Assert.Contains("dry run", dash.SessionActivity[0].Text);
    }
}
