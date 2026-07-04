using System.Linq;
using Avalonia.Headless.XUnit;
using WinButler.Services;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Tests the Dashboard's aggregation. Uses a full <see cref="MainWindowViewModel"/> (which wires
/// the real services but scans nothing on construction) and drives it by populating a Clean
/// category directly.
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
}
