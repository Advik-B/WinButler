using Avalonia.Headless.XUnit;
using WinButler.Services.Definitions;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// The shell's fail-closed response to a bad definitions load: no scanners are constructed
/// (so nothing can be offered for deletion against an empty deny-list) and a persistent error
/// banner is shown.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DefinitionsFallbackTests : MessengerIsolatedTest
{
    [AvaloniaFact]
    public void Failed_definitions_disable_cleaning_and_surface_an_error()
    {
        var shell = new MainWindowViewModel(new DefinitionsProvider(() => null));

        Assert.True(shell.HasDefinitionsError);
        Assert.NotNull(shell.DefinitionsError);
        // No scanners → no Clean categories → nothing can be offered for deletion.
        Assert.Empty(shell.CleanPage.Categories);
        Assert.Null(shell.CleanPage.CacheCategory);
    }

    [AvaloniaFact]
    public void Healthy_definitions_leave_cleaning_enabled()
    {
        var shell = new MainWindowViewModel();

        Assert.False(shell.HasDefinitionsError);
        Assert.Null(shell.DefinitionsError);
        Assert.NotEmpty(shell.CleanPage.Categories);
    }
}
