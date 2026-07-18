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

    /// <summary>Specific app/system junk locations from the known-locations catalog
    /// (<see cref="Services.KnownLocationsScanner"/>): caches, logs and crash dumps.</summary>
    Apps,

    /// <summary>Steam client and per-library junk (shader/download caches, dumps, logs) found by
    /// <see cref="Services.SteamScanner"/> after discovering Steam's install and library folders.</summary>
    Steam,

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

/// <summary>Toast severity — drives which signal color the toast's dot uses.</summary>
public enum ToastKind
{
    Ok,
    Dry,
    Warn,
    Live,
}
