using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// The deny-list must hold on EVERY scan path. Temp and Electron used to bypass
/// <see cref="SafeCaches.IsDenied"/> entirely — anything parked in a temp root (including
/// credential/secret-shaped material) was offered for PERMANENT deletion. These pin the fix.
/// </summary>
public sealed class DenyListScannerTests : IDisposable
{
    private readonly string _root;

    public DenyListScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerDeny_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Temp_scanner_never_offers_denied_paths()
    {
        var root = Path.Combine(_root, "temp-root");
        Directory.CreateDirectory(Path.Combine(root, "plain-junk"));
        File.WriteAllText(Path.Combine(root, "plain-junk", "x.tmp"), "junk");
        Directory.CreateDirectory(Path.Combine(root, ".ssh"));            // deny fragment "\.ssh"
        File.WriteAllText(Path.Combine(root, ".ssh", "id_ed25519"), "key material");
        File.WriteAllText(Path.Combine(root, "secrets.txt"), "hush");     // deny fragment "secret"

        var results = new List<CleanupTarget>();
        new TempScanner().ScanRoot(root, results, default);

        Assert.Contains(results, t => t.FullPath.EndsWith("plain-junk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, t => t.FullPath.Contains(".ssh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, t => t.FullPath.Contains("secrets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Electron_scanner_never_offers_denied_paths()
    {
        // A Squirrel layout whose path hits a deny fragment ("vault") must yield nothing…
        var denied = Path.Combine(_root, "vault-app");
        MakeSquirrelApp(denied);

        var results = new List<CleanupTarget>();
        new ElectronLeftoverScanner().ScanParent(denied, results, default);
        Assert.Empty(results);

        // …while the identical clean layout offers exactly its stale version.
        var clean = Path.Combine(_root, "GoodApp");
        MakeSquirrelApp(clean);

        new ElectronLeftoverScanner().ScanParent(clean, results, default);
        var target = Assert.Single(results);
        Assert.EndsWith("app-1.0.0", target.FullPath);
        Assert.Equal(RiskLevel.Safe, target.Risk);
    }

    private static void MakeSquirrelApp(string parent)
    {
        Directory.CreateDirectory(Path.Combine(parent, "app-1.0.0"));
        Directory.CreateDirectory(Path.Combine(parent, "app-2.0.0"));
        File.WriteAllText(Path.Combine(parent, "Update.exe"), "");
        File.WriteAllText(Path.Combine(parent, "app-1.0.0", "old.dll"), "x");
        File.WriteAllText(Path.Combine(parent, "app-2.0.0", "new.dll"), "x");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
