using Avalonia.Headless.XUnit;
using WinButler.Models;
using WinButler.ViewModels;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Tests the shell's navigation and the single overlay slots (toast + destructive-confirm modal).
/// These touch <c>DispatcherTimer</c> (toast) so they run under the headless UI thread.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ShellTests : MessengerIsolatedTest
{
    [AvaloniaTheory]
    [InlineData("electron")]
    [InlineData("temp")]
    [InlineData("cache")]
    [InlineData("apps")]
    [InlineData("steam")]
    [InlineData("devjunk")]
    [InlineData("redirect")]
    [InlineData("disk")]
    [InlineData("system")]
    [InlineData("dashboard")]
    public void Navigate_selects_the_matching_page_and_sets_the_tag(string tag)
    {
        var shell = new MainWindowViewModel();

        shell.Navigate(tag);

        ViewModelBase expected = tag switch
        {
            "electron" => shell.ElectronPage,
            "temp" => shell.TempPage,
            "cache" => shell.CachePage,
            "apps" => shell.AppsPage,
            "steam" => shell.SteamPage,
            "devjunk" => shell.DevJunkPage,
            "redirect" => shell.RedirectPage,
            "disk" => shell.DiskPage,
            "system" => shell.SystemToolsPage,
            _ => shell.DashboardPage,
        };
        Assert.Same(expected, shell.CurrentPage);
        Assert.Equal(tag, shell.ActiveNavTag);
    }

    [AvaloniaFact]
    public void Unknown_tag_falls_back_to_the_dashboard()
    {
        var shell = new MainWindowViewModel();

        shell.Navigate("nonsense");

        Assert.Same(shell.DashboardPage, shell.CurrentPage);
    }

    [AvaloniaFact]
    public void ShowToast_sets_the_current_toast_slot()
    {
        var shell = new MainWindowViewModel();

        shell.ShowToast("Cleaned up", ToastKind.Ok);

        // Assert the state transition only — the ~3.6s auto-dismiss timer is deliberately not awaited.
        Assert.NotNull(shell.CurrentToast);
        Assert.Equal("Cleaned up", shell.CurrentToast!.Message);
        Assert.Equal(ToastKind.Ok, shell.CurrentToast.Kind);
    }

    [AvaloniaFact]
    public void Confirm_invokes_the_callback_and_clears_the_slot()
    {
        var shell = new MainWindowViewModel();
        var invoked = false;
        shell.RequestConfirm("Delete forever", count: 3, bytes: 1024, onConfirmed: () => invoked = true);

        Assert.NotNull(shell.PendingConfirm);
        shell.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.True(invoked);
        Assert.Null(shell.PendingConfirm);
    }

    [AvaloniaFact]
    public void Cancel_skips_the_callback_and_clears_the_slot()
    {
        var shell = new MainWindowViewModel();
        var invoked = false;
        shell.RequestConfirm("Delete forever", count: 3, bytes: 1024, onConfirmed: () => invoked = true);

        shell.PendingConfirm!.CancelCommand.Execute(null);

        Assert.False(invoked);
        Assert.Null(shell.PendingConfirm);
    }
}
