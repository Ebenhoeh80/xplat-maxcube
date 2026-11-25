using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Domain.ValueObjects;
using MaxCube.Infrastructure.Connectivity;
using MaxCube.Protocol;
using MaxCube.Protocol.Exceptions;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

/// <summary>
/// Sonnet, Runde 3 N1: M-B und M-G waren im Code behoben, aber nicht getestet.
/// Der Transport wird über die Factory ersetzt; es gibt echten TCP- und Cube-Zugriff nicht.
/// </summary>
public class CubeGatewayTests
{
    [Fact]
    public async Task A_non_transport_command_failure_faults_the_session()
    {
        // EOF vor der Antwort ist eine CubeCommunicationFailedException, keine CubeTransportException.
        // Parse-Fehler faulten bewusst nicht; der Worker-EOF dagegen schon.
        var transport = new ScriptedTransport("H:JEQ0001234,1d2a3c,0113", "L:");
        await using var gateway = new CubeGateway(transportFactory: (_, _) => transport);
        await gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        gateway.State.Should().Be(CubeConnectionState.Connected);

        var act = () => gateway.SetRoomTemperatureAsync(
            1, new RadioAddress(0x1D2A3C), new Temperature(21.0));

        await act.Should().ThrowAsync<CubeCommunicationFailedException>();
        gateway.State.Should().Be(CubeConnectionState.Faulted);
        transport.SentLines.Should().Contain(line => line.StartsWith("s:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disposal_sends_q_and_is_idempotent()
    {
        var transport = new ScriptedTransport("H:JEQ0001234,1d2a3c,0113", "L:");
        var gateway = new CubeGateway(transportFactory: (_, _) => transport);
        await gateway.ConnectAsync(new CubeEndpoint("fake", 62910));

        await gateway.DisposeAsync();
        await gateway.DisposeAsync();

        transport.SentLines.Should().ContainSingle().Which.Should().Be("q:");
        gateway.State.Should().Be(CubeConnectionState.Disconnected);
    }

    [Fact]
    public async Task A_transport_that_ignores_cancellation_cannot_block_disconnect()
    {
        var transport = new HangingTransport();
        var gateway = new CubeGateway(transportFactory: (_, _) => transport);
        var connect = gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await gateway.DisconnectAsync();
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        transport.Release.TrySetResult();
        try
        {
            await connect;
        }
        catch
        {
            // Der Connect läuft nach dem erzwungenen Trennen ins Leere; das ist erwartet.
        }

        gateway.State.Should().Be(CubeConnectionState.Faulted);
    }

    [Fact]
    public async Task An_idle_session_refreshes_device_states_with_l()
    {
        var options = new CubeConnectionOptions
        {
            WatchdogInterval = TimeSpan.FromMilliseconds(10),
            IdleTimeout = TimeSpan.FromMilliseconds(20),
            KeepAliveInterval = TimeSpan.FromMilliseconds(60),
        };
        var transport = new RefreshTransport();
        await using var gateway = new CubeGateway(options, (_, _) => transport);
        var refreshed = new TaskCompletionSource<MaxCube.Domain.Aggregates.Cube>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.CubeRefreshed += (_, cube) => refreshed.TrySetResult(cube);

        await gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        try
        {
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Kein l:-Refresh. Gesendet: {string.Join(", ", transport.SentLines)}; State: {gateway.State}; Fehler: {gateway.LastRefreshError}");
        }

        transport.SentLines.Should().Contain(line => line == "l:");
        gateway.State.Should().Be(CubeConnectionState.Connected);
    }

    [Fact]
    public async Task An_unreachable_session_can_reconnect_without_an_explicit_disconnect()
    {
        var options = new CubeConnectionOptions
        {
            WatchdogInterval = TimeSpan.FromMilliseconds(10),
            IdleTimeout = TimeSpan.FromMilliseconds(20),
            KeepAliveInterval = TimeSpan.FromMilliseconds(60),
        };
        var factory = new ReconnectTransportFactory();
        await using var gateway = new CubeGateway(options, factory.Create);
        var unreachable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.SessionUnreachable += (_, _) => unreachable.TrySetResult();

        await gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        await unreachable.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gateway.State.Should().Be(CubeConnectionState.Faulted);

        // Die Anwendung ruft beim Reconnect direkt ConnectAsync - ohne vorheriges Disconnect.
        Func<Task> reconnect = () => gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        await reconnect.Should().NotThrowAsync();
        gateway.State.Should().Be(CubeConnectionState.Connected);
    }

    private sealed class ReconnectTransportFactory
    {
        private int _created;

        public ICubeTransport Create(CubeEndpoint _, CubeConnectionOptions __)
            => Interlocked.Increment(ref _created) == 1 ? new FaultingRefreshTransport() : new RefreshTransport();
    }

    private sealed class FaultingRefreshTransport : ICubeTransport
    {
        private int _reads;

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _reads);
            return ValueTask.FromResult<string?>(index switch
            {
                1 => "H:JEQ0001234,1d2a3c,0113",
                2 => "L:",
                _ => "r:refresh-failed",
            });
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RefreshTransport : ICubeTransport
    {
        private int _reads;

        public List<string> SentLines { get; } = new();

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _reads);
            return ValueTask.FromResult<string?>(
                index == 1 ? "H:JEQ0001234,1d2a3c,0113" : "L:");
        }

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            SentLines.Add(line);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HangingTransport : ICubeTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            Started.TrySetResult();
            // Bewusst ohne CancellationToken: ein defekter Transport darf Shutdown nicht blockieren.
            return new ValueTask(Release.Task);
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedTransport : ICubeTransport
    {
        private readonly Queue<string> _lines;

        public ScriptedTransport(params string[] lines) => _lines = new Queue<string>(lines);

        public List<string> SentLines { get; } = new();

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_lines.Count > 0 ? _lines.Dequeue() : null);

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            SentLines.Add(line);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
