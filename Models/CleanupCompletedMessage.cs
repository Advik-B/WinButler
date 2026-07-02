using System;

namespace WinButler.Models;

/// <summary>Which feature reported a completed run — drives the Session Activity row's icon/label.</summary>
public enum CleanupAction
{
    Clean,
    DevJunk,
    Redirect,
}

/// <summary>
/// Broadcast (via <c>WeakReferenceMessenger</c>) whenever a clean/redirect/dev-junk run finishes,
/// so the Dashboard's Session Activity feed can record it. Sent for dry runs too (tagged), so the
/// feed reflects simulated as well as real actions. The Dashboard is the only subscriber today.
/// </summary>
public sealed record CleanupCompletedMessage(
    CleanupAction Action, long Bytes, int Count, bool DryRun, DateTime Time);
