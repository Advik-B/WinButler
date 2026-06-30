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
}

/// <summary>How a delete is physically carried out.</summary>
public enum DeleteMode
{
    /// <summary>Removed permanently (used for <see cref="RiskLevel.Safe"/> targets).</summary>
    Permanent,

    /// <summary>Sent to the Windows Recycle Bin so it can be restored.</summary>
    RecycleBin,
}
