using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace WinButler.Services;

/// <summary>
/// Velopack auto-update seam. Checks the GitHub Releases feed once per launch, downloads in the
/// background, and applies on an explicit user restart — never silently. Everything no-ops when
/// the app isn't a Velopack install (dev `dotnet run`, CI, headless tests), so the feature is
/// invisible outside a real installed copy.
/// </summary>
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/Advik-B/WinButler";

    private UpdateManager? _manager;
    private bool _managerUnavailable;
    private UpdateInfo? _pending;

    /// <summary>
    /// Created lazily and fail-soft: UpdateManager's constructor throws when the process wasn't
    /// launched through <c>VelopackApp.Build().Run()</c> (headless tests boot the App without
    /// Main), and the updater must never be able to take the shell down.
    /// WINBUTLER_UPDATE_URL overrides the release feed (a local folder or any URL) so an
    /// install→update cycle can be exercised end-to-end without publishing a GitHub release.
    /// </summary>
    private UpdateManager? Manager
    {
        get
        {
            if (_manager is null && !_managerUnavailable)
            {
                try
                {
                    var overrideUrl = Environment.GetEnvironmentVariable("WINBUTLER_UPDATE_URL");
                    _manager = overrideUrl is null
                        ? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false))
                        : new UpdateManager(overrideUrl);
                }
                catch (Exception ex)
                {
                    _managerUnavailable = true;
                    Log.Info("update", $"Updater unavailable: {ex.Message}");
                }
            }
            return _manager;
        }
    }

    /// <summary>False for non-Velopack launches; callers must skip everything else then.</summary>
    public bool IsSupported => Manager?.IsInstalled ?? false;

    /// <summary>
    /// Checks the feed and, if a newer version exists, downloads it (full or delta) so it's ready
    /// to apply. Returns the new version string (e.g. "1.1.0"), or null when already current.
    /// Throws on network/feed errors — callers run this under RunGuardedAsync.
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync()
    {
        if (Manager is not { } manager)
            return null;

        var info = await manager.CheckForUpdatesAsync();
        if (info is null)
        {
            Log.Info("update", "Already on the latest version.");
            return null;
        }

        var version = info.TargetFullRelease.Version.ToString();
        Log.Info("update", $"Update {version} found; downloading.");
        await manager.DownloadUpdatesAsync(info);
        _pending = info;
        Log.Info("update", $"Update {version} downloaded and ready to apply.");
        return version;
    }

    /// <summary>Applies the downloaded update and restarts the app. No-op until a successful
    /// <see cref="CheckAndDownloadAsync"/> has staged one.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is null || Manager is not { } manager)
            return;
        Log.Info("update", $"Applying update {_pending.TargetFullRelease.Version} and restarting.");
        manager.ApplyUpdatesAndRestart(_pending);
    }
}
