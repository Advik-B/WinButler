using Avalonia;
using Avalonia.Media;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// Pushes the chosen <see cref="AccentKind"/>'s precomputed brush/glow set (see
/// Themes/Tokens.Colors.axaml and Themes/Effects.axaml) into the live, mutable
/// "Wb*" resource keys every screen binds to via DynamicResource. Call
/// <see cref="Apply"/> once at startup and again whenever <see cref="AppSettings.Accent"/>
/// changes — it does not subscribe itself, since the app-wide accent change is rare and
/// the call site (menu command / startup) is a clearer place to see the wiring.
/// </summary>
public static class ThemeService
{
    private static readonly string[] BrushKeys =
    {
        "Accent", "AccentOn", "AccentBright", "AccentHover", "Accent600", "Accent700",
        "AccentPress", "AccentDim", "AccentTint", "DotAccent",
    };

    public static void Apply(AccentKind accent)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var prefix = accent == AccentKind.Green ? "WbGreen" : "WbRed";

        // Look up on `app` (the IResourceHost), not `app.Resources` — the precomputed
        // Red/Green palette lives in Tokens.Colors.axaml's Styles-merged dictionary, not the
        // bare Application.Resources dictionary. Resources.TryGetResource only searches that
        // one dictionary and silently misses everything defined via Styles, which made this a
        // no-op for every accent (Red looked "correct" only because it's also the built-in
        // XAML default that Styles resolution already falls back to).
        foreach (var key in BrushKeys)
        {
            if (app.TryGetResource($"{prefix}{key}Brush", null, out var brush) && brush is IBrush)
                app.Resources[$"Wb{key}Brush"] = brush;
        }

        var suffix = accent == AccentKind.Green ? "Green" : "Red";
        foreach (var shadowKey in new[] { "WbGlowAccentSm", "WbGlowAccent", "WbGlowAccentLg" })
        {
            if (app.TryGetResource($"{shadowKey}{suffix}", null, out var shadow))
                app.Resources[$"{shadowKey}Brush"] = shadow!;
        }
    }

    /// <summary>
    /// Resolves a theme brush for C# call sites (converters, custom-drawn controls) from
    /// <see cref="Application.Current"/> — which, unlike Application.Resources.TryGetResource,
    /// cascades into the Styles-merged token dictionaries (see the resource-lookup gotcha in
    /// CLAUDE.md). The hex fallback only exists so headless/unit contexts without the theme
    /// stay functional; the XAML token is the single source of truth.
    /// </summary>
    public static IBrush Brush(string key, string fallbackHex)
    {
        if (Application.Current is { } app
            && app.TryGetResource(key, null, out var value) && value is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
