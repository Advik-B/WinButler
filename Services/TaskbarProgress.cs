using System;
using System.Runtime.InteropServices;

namespace WinButler.Services;

/// <summary>
/// Mirrors the shell status bar onto the Windows taskbar button via the shell's
/// <c>ITaskbarList3</c> progress API (the green fill / marquee Windows paints over an app's
/// taskbar icon). Hand-rolled COM interop in the same spirit as the <c>[DllImport]</c>
/// conventions in <see cref="WinButler.Services.Mft.NtfsNative"/> / <see cref="Junction"/> —
/// there is no CsWin32 in this project and the TFM is plain <c>net10.0</c>.
///
/// Fail-closed and never throws: if COM init fails (no shell, headless, sandbox) the whole
/// instance becomes an inert no-op, matching the app-wide "diagnostics never crash" posture.
/// Only constructed from window code-behind under the desktop lifetime, so headless tests
/// never load COM.
/// </summary>
internal sealed class TaskbarProgress
{
    // TBPFLAG — the progress states we use (Windows renders TBPF_NORMAL green, matching the theme).
    private const int TBPF_NOPROGRESS = 0x0;
    private const int TBPF_INDETERMINATE = 0x1;
    private const int TBPF_NORMAL = 0x2;

    // Denominator for the determinate fill; ProgressValue (0..1) is scaled onto this.
    private const ulong ProgressScale = 1000;

    private readonly IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private bool _dead;      // true once init failed — stay an inert no-op forever
    private bool _loggedFailure;

    public TaskbarProgress(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Clear taskbar progress (shell bar hidden / operation finished).</summary>
    public void SetNone() => Guard(t => t.SetProgressState(_hwnd, TBPF_NOPROGRESS));

    /// <summary>Marquee, mirroring the in-app indeterminate spinner (scans / index builds).</summary>
    public void SetIndeterminate() => Guard(t => t.SetProgressState(_hwnd, TBPF_INDETERMINATE));

    /// <summary>Determinate green fill mirroring the 0..1 in-app bar (delete / move loops).</summary>
    public void SetNormal(double fraction01)
    {
        var clamped = fraction01 < 0 ? 0 : fraction01 > 1 ? 1 : fraction01;
        Guard(t =>
        {
            t.SetProgressState(_hwnd, TBPF_NORMAL);
            t.SetProgressValue(_hwnd, (ulong)Math.Round(clamped * ProgressScale), ProgressScale);
        });
    }

    /// <summary>Runs a COM action against a lazily-created taskbar object, swallowing every
    /// failure. The first failure kills the instance so we don't retry (or log-spam) forever.</summary>
    private void Guard(Action<ITaskbarList3> action)
    {
        if (_dead || _hwnd == IntPtr.Zero) return;
        try
        {
            action(EnsureTaskbar());
        }
        catch (Exception ex)
        {
            _dead = true;
            _taskbar = null;
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log.Warn("taskbar", "Taskbar-icon progress disabled (COM call failed).", ex);
            }
        }
    }

    private ITaskbarList3 EnsureTaskbar()
    {
        if (_taskbar is null)
        {
            _taskbar = (ITaskbarList3)new TaskbarListClass();
            _taskbar.HrInit();
        }
        return _taskbar;
    }

    // --- COM interop ---------------------------------------------------------------------------

    // CLSID_TaskbarList — the shell object that implements ITaskbarList3.
    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListClass { }

    // ITaskbarList3 (IID EA1AFB91-…). All inherited vtable slots must be declared IN ORDER up to
    // the methods we call, or the layout shifts and we invoke the wrong function. We only call
    // HrInit / SetProgressValue / SetProgressState; the earlier slots are placeholders and the
    // slots after SetProgressState are omitted (never called).
    [ComImport,
     Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        // ITaskbarList3 (only these two are used)
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
    }
}
