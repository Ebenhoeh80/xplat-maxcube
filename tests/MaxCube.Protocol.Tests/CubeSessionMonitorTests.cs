using FluentAssertions;
using MaxCube.Protocol.Session;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class CubeSessionMonitorTests
{
    [Fact]
    public void Idle_and_keepalive_states_follow_the_fake_clock()
    {
        var clock = new FakeTimeProvider();
        var options = new CubeConnectionOptions
        {
            TimeProvider = clock,
            WatchdogInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(30),
            KeepAliveInterval = TimeSpan.FromSeconds(900),
            UnreachableTimeout = TimeSpan.FromHours(6),
        };
        var monitor = new CubeSessionMonitor(options);

        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.Active);

        clock.Advance(TimeSpan.FromSeconds(29));
        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.Active);

        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.Idle);

        clock.Advance(TimeSpan.FromSeconds(870));
        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.KeepAliveDue);

        clock.Advance(TimeSpan.FromHours(6));
        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.Unreachable);

        monitor.RecordActivity();
        monitor.Evaluate(clock.GetUtcNow()).Should().Be(CubeSessionState.Active);
    }

    [Fact]
    public async Task Watchdog_loop_fires_on_the_fake_clock_and_can_be_rescheduled()
    {
        var clock = new FakeTimeProvider();
        var options = new CubeConnectionOptions
        {
            TimeProvider = clock,
            WatchdogInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(10),
            KeepAliveInterval = TimeSpan.FromMinutes(1),
            UnreachableTimeout = TimeSpan.FromHours(1),
        };
        var monitor = new CubeSessionMonitor(options);
        var watchdogCalls = 0;
        var keepAliveCalls = 0;
        using var cancellation = new CancellationTokenSource();

        var loop = monitor.RunAsync(
            _ => { Interlocked.Increment(ref watchdogCalls); return Task.CompletedTask; },
            _ => { Interlocked.Increment(ref keepAliveCalls); return Task.CompletedTask; },
            cancellation.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => Volatile.Read(ref watchdogCalls) > 0);
        watchdogCalls.Should().BeGreaterThan(0);
        keepAliveCalls.Should().Be(0);

        monitor.RecordActivity();
        clock.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => Volatile.Read(ref keepAliveCalls) > 0);
        keepAliveCalls.Should().BeGreaterThan(0);

        cancellation.Cancel();
        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // expected: the watchdog stops on cancellation
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
