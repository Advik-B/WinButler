using System;
using System.IO;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root;

    public RecycleBinTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerRB_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Recycling_a_junction_removes_the_link_but_never_the_target()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "sentinel.txt");
        File.WriteAllText(sentinel, "must survive");

        var link = Path.Combine(_root, "link");
        Junction.Create(link, target);

        RecycleBin.Send(link);

        Assert.False(Directory.Exists(link));      // the link itself is gone
        Assert.True(Directory.Exists(target));     // the data it pointed at is untouched
        Assert.Equal("must survive", File.ReadAllText(sentinel));
    }

    [Fact]
    public void Recycling_a_plain_file_succeeds()
    {
        var file = Path.Combine(_root, "WinButler_recycle_test.txt");
        File.WriteAllText(file, "tiny");

        RecycleBin.Send(file);

        Assert.False(File.Exists(file)); // moved to the Recycle Bin, not present here anymore
    }

    [Fact]
    public void Recycling_a_missing_path_is_a_no_op()
    {
        RecycleBin.Send(Path.Combine(_root, "does-not-exist"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
