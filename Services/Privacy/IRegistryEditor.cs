using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WinButler.Services.Privacy;

/// <summary>
/// Narrow seam over the handful of HKCU registry operations <see cref="PrivacyCleaner"/> needs, so
/// tests can drive the cleaner without touching the real registry. All subkeys are relative to
/// <c>HKEY_CURRENT_USER</c>.
/// </summary>
public interface IRegistryEditor
{
    /// <summary>Value names directly under the subkey; empty if the key doesn't exist.</summary>
    IReadOnlyList<string> GetValueNames(string subKey);

    /// <summary>Deletes one value; a no-op if the key or value is absent.</summary>
    void DeleteValue(string subKey, string valueName);
}

/// <summary>Real HKCU-backed editor. Registry has no Recycle Bin — deletes are permanent.</summary>
public sealed class HkcuRegistryEditor : IRegistryEditor
{
    public IReadOnlyList<string> GetValueNames(string subKey)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();

        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public void DeleteValue(string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
