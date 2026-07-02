using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Services;

namespace WinButler.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
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
