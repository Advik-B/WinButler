using Avalonia;
using Avalonia.Media;

namespace WinButler.Services;

/// <summary>
/// C#-side access to the "Duly Doted" theme tokens (Themes/Tokens.*.axaml). The old
/// red/green accent-swap machinery (Apply) was removed when the app went green-only —
/// the WbAccent* keys now hold the green values directly in XAML.
/// </summary>
public static class ThemeService
{
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
