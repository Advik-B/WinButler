using System;
using Avalonia.Controls;
using WinButler.ViewModels;

namespace WinButler.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>Scan-by-default: kick off the full sweep once, on first paint, so the dashboard
    /// and every page show real numbers without the user pressing anything. Kept in the window
    /// code-behind (not the shell VM constructor) so headless VM tests never trigger a real MFT
    /// read. Skipped when definitions failed to load (fail-closed — nothing to scan).</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened; // run exactly once
        if (DataContext is MainWindowViewModel vm
            && !vm.HasDefinitionsError
            && vm.RescanAllCommand.CanExecute(null))
        {
            vm.RescanAllCommand.Execute(null);
        }
    }
}
