using FluentAssertions;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Protocol.Tests;

/// <summary>
/// End-to-end AP-1.1 + AP-1.2 + AP-1.3 over a loopback fake cube (no hardware).
/// The fake is a test harness; it is not a hardware golden capture.
/// </summary>
public class CubeSessionIntegrationTests
{
    [Fact]
    public async Task Connect_import_state_then_execute_commands_in_order()
    {
        await using var cube = new LoopbackFakeCube();
        var options = new CubeConnectionOptions
        {
            Port = cube.Port,
            TimeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero)),
        };
        await using var transport = new TcpCubeTransport(options);
        await transport.ConnectAsync();

        var hello = await new CubeHandshaker(options).PerformAsync(transport);
        hello.FirmwareVersion.Should().Be(0x113);

        var state = await new StateReceiver(options).ReceiveAsync(transport, hello);
        state.IsComplete.Should().BeTrue();
        state.GetDeviceConfiguration(0x1D2A3C).Should().NotBeNull();
        state.MetadataBlocks.Should().ContainKey(0);

        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();
        (await queue.ExecuteAsync(new RawCommand("u:http://localhost", "A:"))).Should().Be("A:");
        (await queue.ExecuteAsync(new RawCommand("l:", "L:"))).Should().StartWith("L:");
        (await queue.ExecuteAsync(new RawCommand("s:/wDcAA==", "S:"))).Should().Be("S:05,0,1f");
        await queue.ExecuteAsync(new RawCommand("q:", expectsResponse: false));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (ReceivedCount(cube) < 4)
        {
            await Task.Delay(10, timeout.Token);
        }

        ReceivedCommands(cube).Should().Equal("u:http://localhost", "l:", "s:/wDcAA==", "q:");
        queue.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task A_wrong_response_prefix_is_reported_instead_of_being_treated_as_success()
    {
        await using var cube = new LoopbackFakeCube();
        var options = new CubeConnectionOptions { Port = cube.Port };
        await using var transport = new TcpCubeTransport(options);
        await transport.ConnectAsync();
        await new CubeHandshaker(options).PerformAsync(transport);
        await new StateReceiver(options).ReceiveAsync(
            transport,
            HelloParser.Parse(
                "H:JEQ0001234,1d2a3c,0113,00,00,05,1f,1a0b18,0c1e,3,0002",
                options.TimeProvider));
        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();

        var act = () => queue.ExecuteAsync(new RawCommand("u:http://localhost", "L:"));

        await act.Should().ThrowAsync<UnexpectedCubeResponseException>().WithMessage("*Expected L:*");
        queue.IsFaulted.Should().BeFalse();
        (await queue.ExecuteAsync(new RawCommand("u:again", "A:"))).Should().Be("A:");
    }

    private static int ReceivedCount(LoopbackFakeCube cube)
    {
        lock (cube.ReceivedCommands)
        {
            return cube.ReceivedCommands.Count;
        }
    }

    private static string[] ReceivedCommands(LoopbackFakeCube cube)
    {
        lock (cube.ReceivedCommands)
        {
            return cube.ReceivedCommands.ToArray();
        }
    }

    private sealed class RawCommand : CubeCommand<string>
    {
        private readonly string _wireLine;
        private readonly string? _expectedPrefix;

        public RawCommand(string wireLine, string? expectedPrefix = null, bool expectsResponse = true)
        {
            _wireLine = wireLine;
            _expectedPrefix = expectedPrefix;
            ExpectsResponse = expectsResponse;
        }

        public override string WireLine => _wireLine;

        public override bool ExpectsResponse { get; }

        protected override string ParseResponse(string? line)
        {
            if (_expectedPrefix is null || line?.StartsWith(_expectedPrefix, StringComparison.Ordinal) == true)
            {
                return line ?? string.Empty;
            }

            throw new UnexpectedCubeResponseException($"Expected {_expectedPrefix}, got '{line}'.");
        }
    }
}
