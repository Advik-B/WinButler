using System;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Covers <see cref="EmbeddedScript"/> — building an -EncodedCommand invocation entirely in
/// memory, with a readable Display for dry-run previews and the runner's "> ..." line.
/// </summary>
public sealed class EmbeddedScriptTests
{
    [Fact]
    public void RunCommand_encodes_the_script_and_never_touches_disk()
    {
        var command = EmbeddedScript.RunCommand("RemoveGhostDevices.ps1");

        Assert.Equal("powershell.exe", command.FileName);
        Assert.Contains("-EncodedCommand", command.Arguments);
        Assert.Contains("-NoProfile", command.Arguments);
    }

    [Fact]
    public void Display_shows_the_script_name_not_the_encoded_payload()
    {
        var command = EmbeddedScript.RunCommand("RemoveGhostDevices.ps1");

        Assert.Contains("RemoveGhostDevices.ps1", command.Display);
        Assert.DoesNotContain("EncodedCommand", command.Display);
    }

    [Fact]
    public void Unknown_script_name_throws_rather_than_silently_running_nothing()
    {
        Assert.Throws<InvalidOperationException>(() => EmbeddedScript.RunCommand("DoesNotExist.ps1"));
    }

    /// <summary>Scripts/ also holds scripts.json. Whatever this resolves gets executed, so the
    /// lookup must not reach a non-.ps1 resource even when asked for one by name.</summary>
    [Fact]
    public void A_non_script_resource_in_the_scripts_folder_is_not_executable()
    {
        Assert.Throws<InvalidOperationException>(() => EmbeddedScript.RunCommand("scripts.json"));
    }

    [Fact]
    public void Mode_changes_the_encoded_payload_but_not_the_display()
    {
        var plain = EmbeddedScript.RunCommand("RemoveGhostDevices.ps1");
        var withMode = EmbeddedScript.RunCommand("RemoveGhostDevices.ps1", "List");

        Assert.NotEqual(plain.Arguments, withMode.Arguments); // different payload
        Assert.Equal(plain.Display, withMode.Display);        // same readable preview
    }

    /// <summary>The mode is interpolated into the script body ($Mode = '...') and the resulting
    /// script runs elevated, so anything that could escape the quotes must be refused outright
    /// rather than encoded.</summary>
    [Theory]
    [InlineData("List'; Remove-Item C:\\ -Recurse #")]
    [InlineData("List\nRemove-Item")]
    [InlineData("List'")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("1StartsWithDigit")]
    public void Invalid_mode_throws_rather_than_encoding(string mode)
    {
        Assert.Throws<InvalidOperationException>(() => EmbeddedScript.RunCommand("RemoveGhostDevices.ps1", mode));
    }
}
