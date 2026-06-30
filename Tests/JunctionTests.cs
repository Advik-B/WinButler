using System;
using System.IO;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Exercises the real reparse-point primitive. Source and target live under the temp dir
/// (junctions resolve same-volume too), so the test is self-contained. Windows-only.
/// </summary>
public sealed class JunctionTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _target;

    public JunctionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinButlerTests_" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "src");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "hello.txt"), "hello-from-target");
    }

    [Fact]
    public void Create_then_read_and_remove_roundtrip()
    {
        if (!OperatingSystem.IsWindows()) return;

        Junction.Create(_source, _target);

        Assert.True(Junction.IsJunction(_source));
        Assert.Equal(
            _target.TrimEnd('\\'),
            Junction.GetTarget(_source)?.TrimEnd('\\'),
            ignoreCase: true);

        // Read through the junction.
        Assert.Equal("hello-from-target", File.ReadAllText(Path.Combine(_source, "hello.txt")));

        // Write through the junction lands in the real target.
        File.WriteAllText(Path.Combine(_source, "world.txt"), "written");
        Assert.True(File.Exists(Path.Combine(_target, "world.txt")));

        // Removing the junction must preserve the target data.
        Junction.Remove(_source);
        Assert.False(Directory.Exists(_source));
        Assert.True(File.Exists(Path.Combine(_target, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(_target, "world.txt")));
    }

    [Fact]
    public void Remove_refuses_a_real_directory()
    {
        if (!OperatingSystem.IsWindows()) return;

        // _target is a real directory, not a junction → Remove must refuse (data-loss guard).
        Assert.Throws<IOException>(() => Junction.Remove(_target));
        Assert.True(Directory.Exists(_target));
    }

    [Fact]
    public void IsJunction_is_false_for_a_plain_directory()
    {
        Assert.False(Junction.IsJunction(_target));
    }

    public void Dispose()
    {
        try
        {
            if (Junction.IsJunction(_source))
                Junction.Remove(_source);
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }
}
