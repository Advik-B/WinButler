using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;
using WinButler.Services;
using Xunit;

namespace WinButler.Tests;

/// <summary>Drives the real process runner with harmless <c>cmd.exe</c> commands.</summary>
public sealed class SystemActionRunnerTests
{
    // A thread-safe IProgress sink (the runner reports from process-event threads).
    private sealed class Collector : IProgress<string>
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = new();
        public void Report(string value) { lock (_gate) _lines.Add(value); }
        public IReadOnlyList<string> Lines { get { lock (_gate) return _lines.ToList(); } }
    }

    [Fact]
    public async Task Captures_output_and_reports_success()
    {
        var sink = new Collector();
        var steps = new[] { new SystemCommand("cmd.exe", "/c echo winbutler-ok") };

        var exit = await new SystemActionRunner().RunAsync(steps, sink, default);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Lines, l => l.Contains("winbutler-ok"));
    }

    [Fact]
    public async Task Stops_at_the_first_failing_step()
    {
        var sink = new Collector();
        var steps = new[]
        {
            new SystemCommand("cmd.exe", "/c exit 3"),
            new SystemCommand("cmd.exe", "/c echo should-not-run"),
        };

        var exit = await new SystemActionRunner().RunAsync(steps, sink, default);

        Assert.Equal(3, exit);
        Assert.DoesNotContain(sink.Lines, l => l.Contains("should-not-run"));
    }

    [Fact]
    public async Task Cancellation_stops_a_long_running_command()
    {
        var sink = new Collector();
        // ~20s ping; we cancel almost immediately and expect the wait to throw.
        var steps = new[] { new SystemCommand("cmd.exe", "/c ping -n 20 127.0.0.1") };
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SystemActionRunner().RunAsync(steps, sink, cts.Token));
    }
}
