using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
using WinButler.Services.Steam;
using Xunit;

namespace WinButler.Tests;

public sealed class VdfLibraryParserTests
{
    [Fact]
    public void Extracts_every_library_path_and_normalises_separators()
    {
        // Steam writes backslashes doubled and lists the install itself as library 0.
        var vdf = "\"libraryfolders\"\n{\n" +
                  "\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t}\n" +
                  "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:/SteamLibrary\"\n\t}\n}\n";

        var paths = VdfLibraryParser.ParseLibraryPaths(vdf);

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", paths);
        Assert.Contains(@"D:\SteamLibrary", paths);
    }

    [Fact]
    public void Empty_or_garbage_input_yields_no_paths()
    {
        Assert.Empty(VdfLibraryParser.ParseLibraryPaths(""));
        Assert.Empty(VdfLibraryParser.ParseLibraryPaths("not a vdf at all"));
    }
}

public sealed class SteamLocatorTests
{
    [Fact]
    public void Null_registry_value_means_not_installed()
    {
        Assert.Null(new SteamLocator(() => null).FindSteamPath());
        Assert.Null(new SteamLocator(() => "   ").FindSteamPath());
    }

    [Fact]
    public void Forward_slashes_are_normalised()
    {
        Assert.Equal(@"C:\Program Files (x86)\Steam",
            new SteamLocator(() => "C:/Program Files (x86)/Steam/").FindSteamPath());
    }

    [Fact]
    public void A_throwing_reader_is_swallowed()
    {
        Assert.Null(new SteamLocator(() => throw new InvalidOperationException()).FindSteamPath());
    }
}

public sealed class SteamScannerTests : IDisposable
{
    private readonly string _root;

    public SteamScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerSteam_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private static void WriteFile(string path, string content = "data")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Discovers_libraries_from_the_vdf_plus_the_install_dir()
    {
        var steam = Path.Combine(_root, "Steam");
        var lib2 = Path.Combine(_root, "SteamLibrary");
        Directory.CreateDirectory(steam);
        Directory.CreateDirectory(lib2);

        var vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n" +
                  "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t}\n}\n";
        WriteFile(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), vdf);

        var libs = new SteamScanner(SafeCaches.FromBundled(), new SteamLocator(() => steam)).DiscoverLibraries(steam);

        Assert.Contains(steam, libs);
        Assert.Contains(lib2, libs);
    }

    [Fact]
    public async Task Flags_library_caches_with_the_right_risk_and_skips_denied_paths()
    {
        var steam = Path.Combine(_root, "Steam");
        WriteFile(Path.Combine(steam, "steamapps", "shadercache", "game1", "shaders.bin"));
        WriteFile(Path.Combine(steam, "steamapps", "workshop", "downloads", "123", "part.bin"));
        WriteFile(Path.Combine(steam, "logs", "connection.txt"));
        WriteFile(Path.Combine(steam, "crash.mdmp"));
        // A credential-shaped path parked in a swept folder must never be offered.
        WriteFile(Path.Combine(steam, "logs", ".ssh", "id_ed25519"), "key");

        var results = (await new SteamScanner(SafeCaches.FromBundled(), new SteamLocator(() => steam))
            .ScanAsync())
            .Where(t => t.FullPath.StartsWith(steam, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Contains(results, t => t.FullPath.Contains("shadercache") && t.Risk == RiskLevel.Safe);
        Assert.Contains(results, t => t.FullPath.EndsWith("crash.mdmp") && t.Risk == RiskLevel.Safe);
        // Workshop downloads restart if deleted → Caution (recycle bin, not auto-selected).
        Assert.Contains(results, t => t.FullPath.Contains("workshop") && t.Risk == RiskLevel.Caution);
        Assert.DoesNotContain(results, t => t.FullPath.Contains(".ssh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_steam_install_yields_no_library_targets()
    {
        // Locator returns null → only user-level caches (if any on this box) are considered; none
        // are under our temp root, so nothing here.
        var results = (await new SteamScanner(SafeCaches.FromBundled(), new SteamLocator(() => null))
            .ScanAsync())
            .Where(t => t.FullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(results);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
