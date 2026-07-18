using System.Reflection;

namespace WinButler.Services;

/// <summary>
/// Assembly-derived version info, so UI version strings track the csproj
/// <c>&lt;Version&gt;</c> instead of drifting as hardcoded literals.
/// </summary>
public static class AppInfo
{
    /// <summary>"v{major}.{minor}", e.g. "v0.9" — the sidebar wordmark tag.</summary>
    public static string ShortVersion { get; } = Compute();

    private static string Compute()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "v?" : $"v{v.Major}.{v.Minor}";
    }
}
