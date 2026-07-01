namespace WinButler.Models;

/// <summary>
/// How dangerous it is to delete a target. Drives the deletion policy:
/// <see cref="Safe"/> is permanently deleted, everything else goes to the Recycle Bin.
/// </summary>
public enum RiskLevel
{
    /// <summary>Trivially regenerable (temp files, package download caches).</summary>
    Safe,

    /// <summary>Regenerable but slow/annoying, or a "cache"-named folder we are not certain about.</summary>
    Caution,

    /// <summary>Could hold real user data; only ever recycled, never permanently removed.</summary>
    Risky,
}

/// <summary>The kind of cleanup a <see cref="CleanupTarget"/> belongs to.</summary>
public enum CleanupCategory
{
    ElectronLeftover,
    Temp,
    Cache,

    /// <summary>Reclaimable subpath found under a dev-tool root by <see cref="Services.DevJunkAggregator"/>.
    /// Not driven by an <see cref="Services.IScanner"/> or shown in <see cref="ViewModels.CleanPageViewModel"/>'s
    /// category list — it's a distinct aggregation surfaced on the Dev Junk screen.</summary>
    DevJunk,
}

/// <summary>How a delete is physically carried out.</summary>
public enum DeleteMode
{
    /// <summary>Removed permanently (used for <see cref="RiskLevel.Safe"/> targets).</summary>
    Permanent,

    /// <summary>Sent to the Windows Recycle Bin so it can be restored.</summary>
    RecycleBin,
}

/// <summary>The app's single chromatic accent — the "Duly Doted" theme's one configurable knob.</summary>
public enum AccentKind
{
    Red,
    Green,
}

/// <summary>Toast severity — drives which signal color the toast's dot uses.</summary>
public enum ToastKind
{
    Ok,
    Dry,
    Warn,
    Live,
}
