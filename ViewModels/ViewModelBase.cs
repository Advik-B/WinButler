using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>A pending destructive-action confirmation: what's about to happen, how much,
/// and a one-line breakdown (e.g. permanent-vs-Recycle-Bin split).</summary>
public sealed record ConfirmRequest(string Title, int Count, long Bytes, string? Detail)
{
    /// <summary>Builds a confirm from a set of delete targets, summing size and spelling out
    /// the hybrid-delete routing (permanent vs Recycle Bin) so the user sees what's recoverable.</summary>
    public static ConfirmRequest ForDeletion(string title, IReadOnlyList<CleanupTarget> targets)
    {
        long bytes = 0;
        int permanent = 0, recycle = 0;
        foreach (var t in targets)
        {
            bytes += t.SizeBytes;
            if (t.DeleteMode == DeleteMode.Permanent) permanent++; else recycle++;
        }

        string detail = (permanent, recycle) switch
        {
            ( > 0, > 0) => $"{permanent} deleted permanently · {recycle} to Recycle Bin",
            ( > 0, 0) => $"{permanent} deleted permanently",
            (0, > 0) => $"{recycle} to Recycle Bin",
            _ => "",
        };
        return new ConfirmRequest(title, targets.Count, bytes, detail);
    }
}

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Asks the host to confirm a destructive action, returning true to proceed. Wired by the
    /// shell to the confirm modal; unset (as in headless tests) it auto-confirms, so existing
    /// tests that invoke clean/redirect commands directly keep working unchanged.
    /// </summary>
    public Func<ConfirmRequest, Task<bool>>? ConfirmInteraction { get; set; }

    protected Task<bool> ConfirmAsync(ConfirmRequest request) =>
        ConfirmInteraction?.Invoke(request) ?? Task.FromResult(true);

    /// <summary>
    /// Reports long-operation progress to the shell's status-bar bar. Wired by the shell (like
    /// <see cref="ConfirmInteraction"/>); unset in headless tests, where it's a harmless no-op.
    /// First arg is the status line (null clears/hides the bar); second is the fraction 0..1
    /// (null = indeterminate spinner).
    /// </summary>
    public Action<string?, double?>? ShellProgress { get; set; }

    /// <summary>Shows the status-bar bar: determinate if <paramref name="fraction"/> is given,
    /// else indeterminate.</summary>
    protected void ReportProgress(string text, double? fraction = null) => ShellProgress?.Invoke(text, fraction);

    /// <summary>Hides the status-bar bar.</summary>
    protected void ClearProgress() => ShellProgress?.Invoke(null, null);

    /// <summary>
    /// Runs an async command body so that no exception ever reaches the UI-thread
    /// SynchronizationContext, where CommunityToolkit's AsyncRelayCommand would rethrow it
    /// and crash the process. Cancellation reads as a normal outcome; anything else is
    /// logged and surfaced through <paramref name="setStatus"/> in the page's status slot.
    /// Callers keep their own IsBusy bracketing around this call.
    /// </summary>
    protected async Task RunGuardedAsync(Func<Task> body, Action<string> setStatus, string failureContext)
    {
        try
        {
            await body();
        }
        catch (OperationCanceledException)
        {
            setStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error(GetType().Name, $"{failureContext}: {ex.Message}", ex);
            setStatus($"{failureContext}: {ex.Message}");
        }
    }
}
