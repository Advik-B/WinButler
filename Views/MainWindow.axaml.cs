using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using WinButler.Services;
using WinButler.ViewModels;

namespace WinButler.Views;

public partial class MainWindow : Window
{
    private TaskbarProgress? _taskbar;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>Scan-by-default: kick off the full sweep once, on first paint, so the dashboard
    /// and every page show real numbers without the user pressing anything. Kept in the window
    /// code-behind (not the shell VM constructor) so headless VM tests never trigger a real MFT
    /// read. Skipped when definitions failed to load (fail-closed — nothing to scan).
    ///
    /// Also the seam where we mirror the shell status bar onto the taskbar icon: the native
    /// window (and thus a valid HWND) exists by the time Opened fires.</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened; // run exactly once

        if (DataContext is not MainWindowViewModel vm)
            return;

        WireTaskbarProgress(vm);

        if (!vm.HasDefinitionsError && vm.RescanAllCommand.CanExecute(null))
            vm.RescanAllCommand.Execute(null);

        // Once-per-launch update check; guarded inside (logs + swallows failures, no-ops on
        // non-Velopack launches), so fire-and-forget is safe here.
        _ = vm.CheckForUpdatesAsync();
    }

    /// <summary>Mirror the shell's four progress properties onto the Windows taskbar button so
    /// scan/clean progress is visible even when WinButler is minimized. The VM stays
    /// window-agnostic — we observe its observable properties rather than changing it. No-op if
    /// the HWND isn't available; the service itself never throws.</summary>
    private void WireTaskbarProgress(MainWindowViewModel vm)
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            return;

        _taskbar = new TaskbarProgress(hwnd);
        _taskbar.SetNone(); // clear any stale state from a prior process

        vm.PropertyChanged += OnVmProgressChanged;
        PushTaskbarState(vm); // seed from the current state
    }

    private void OnVmProgressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainWindowViewModel.IsProgressActive)
            or nameof(MainWindowViewModel.IsProgressIndeterminate)
            or nameof(MainWindowViewModel.ProgressValue)))
            return;

        if (sender is not MainWindowViewModel vm)
            return;

        // COM must be called from the UI (STA) thread; a page may report progress from a
        // background continuation, so marshal when we're not already on it.
        if (Dispatcher.UIThread.CheckAccess())
            PushTaskbarState(vm);
        else
            Dispatcher.UIThread.Post(() => PushTaskbarState(vm));
    }

    /// <summary>Reads the whole shell progress state and pushes the matching taskbar state —
    /// robust to <c>SetProgress</c> setting its properties one at a time.</summary>
    private void PushTaskbarState(MainWindowViewModel vm)
    {
        if (_taskbar is null)
            return;

        if (!vm.IsProgressActive)
            _taskbar.SetNone();
        else if (vm.IsProgressIndeterminate)
            _taskbar.SetIndeterminate();
        else
            _taskbar.SetNormal(vm.ProgressValue);
    }
}
