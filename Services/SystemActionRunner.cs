using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Runs a <see cref="SystemAction"/>'s external commands in order, streaming their stdout/stderr line
/// by line to an <see cref="IProgress{T}"/> sink so the UI can show live output. Cancellation kills
/// the running child. Every command launch is logged. This is the execution engine behind the System
/// Tools page — it only launches processes; policy (dry-run, confirmation) lives in the ViewModel.
/// </summary>
public class SystemActionRunner
{
    /// <summary>Runs each step in sequence. Returns the exit code of the first step that fails
    /// (non-zero), or 0 if all succeed. Stops at the first failure. Virtual so tests can fake it.</summary>
    public virtual async Task<int> RunAsync(
        IReadOnlyList<SystemCommand> steps, IProgress<string> output, CancellationToken ct = default)
    {
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            int exit = await RunOneAsync(step, output, ct).ConfigureAwait(false);
            if (exit != 0)
            {
                output.Report($"[exit {exit}] {step.Display}");
                return exit;
            }
        }
        return 0;
    }

    private static async Task<int> RunOneAsync(SystemCommand step, IProgress<string> output, CancellationToken ct)
    {
        output.Report($"> {step.Display}");
        Log.Info("system-action", $"Running: {step.Display}");

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = step.FileName,
                Arguments = step.Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Report(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Report(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            // Missing tool / access denied — surface it rather than crashing the sequence.
            output.Report($"[failed to start] {step.Display}: {ex.Message}");
            Log.Error("system-action", $"Failed to start: {step.Display}", ex);
            return -1;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        return proc.ExitCode;
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
    }
}
