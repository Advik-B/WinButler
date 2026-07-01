using System.Collections.Generic;
using System.Linq;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>One Squirrel/Electron install's old versions, grouped for the card+expand layout
/// (mockup: app mark, name, path, "CURRENT vX.Y.Z · KEPT" badge, old-versions total+count).</summary>
public sealed class ElectronGroupViewModel
{
    public string Name { get; }
    public string Mark { get; }
    public string Path { get; }
    public string CurrentVersionLabel { get; }
    public IReadOnlyList<CleanupTargetViewModel> OldVersions { get; }
    public int OldCount => OldVersions.Count;
    public long OldBytes => OldVersions.Sum(v => v.SizeBytes);
    public string OldBytesText => SizeFormatter.Format(OldBytes);

    public ElectronGroupViewModel(string name, IReadOnlyList<CleanupTargetViewModel> oldVersions)
    {
        Name = name;
        Path = System.IO.Path.GetDirectoryName(oldVersions.FirstOrDefault()?.FullPath) ?? "";
        CurrentVersionLabel = oldVersions.FirstOrDefault()?.CurrentVersionLabel ?? "";
        OldVersions = oldVersions;

        // Two-letter mark from the app name, e.g. "GitHubDesktop" -> "GH".
        var letters = name.Where(char.IsLetter).ToArray();
        Mark = letters.Length >= 2
            ? new string(new[] { char.ToUpperInvariant(letters[0]), char.ToUpperInvariant(letters[1]) })
            : name.ToUpperInvariant();
    }
}
