using System;
using System.Collections.Generic;
using System.Linq;

namespace WinButler.Services.Steam;

/// <summary>
/// Minimal parser for Steam's <c>steamapps\libraryfolders.vdf</c>. It extracts every library's
/// <c>"path"</c> value — enough to enumerate all Steam libraries (the main install lists itself as
/// library 0). A full VDF parser is overkill; this reads the quoted key/value pairs line by line.
/// </summary>
public static class VdfLibraryParser
{
    /// <summary>Returns the distinct, normalised library directory paths declared in the VDF text.</summary>
    public static IReadOnlyList<string> ParseLibraryPaths(string vdf)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(vdf))
            return result;

        foreach (var line in vdf.Split('\n'))
        {
            var tokens = ExtractQuotedTokens(line);
            // A library path line is:  "path"    "D:\\SteamLibrary"
            if (tokens.Count >= 2 && tokens[0].Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                var path = Normalize(tokens[1]);
                if (path.Length > 0)
                    result.Add(path);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractQuotedTokens(string line)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0)
                    break;
                tokens.Add(line.Substring(i + 1, end - i - 1));
                i = end + 1;
            }
            else
            {
                i++;
            }
        }
        return tokens;
    }

    // VDF escapes backslashes as "\\"; also accept forward slashes. Collapse to a clean Windows path.
    private static string Normalize(string raw) =>
        raw.Replace("\\\\", "\\").Replace('/', '\\').TrimEnd('\\');
}
