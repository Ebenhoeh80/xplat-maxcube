using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class StateReceiverTests
{
    private static readonly CubeHello Hello = HelloParser.Parse(
        "H:JEQ0001234,1d2a3c,111,00,00,05,1f,1a0b18,0c1e,3,0002",
        new FixedTimeProvider(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Collects_cube_and_device_configurations_metadata_and_device_list_until_L()
    {
        var cubeConfig = B64(new byte[] { 1, 0, 0x1d, 0x2a, 0x3c, 2 });
        var deviceConfig = B64(new byte[] { 1, 0, 0x1d, 0x2a, 0x01, 1 });
        var metadata = B64(Encoding.UTF8.GetBytes("{\"rooms\":[]}"));
        var deviceList = B64(new byte[] { 6, 0x1d, 0x2a, 0x01, 0, 0x06 });
        var transport = new ScriptedCubeTransport(
            $"C:1d2a3c,{cubeConfig}",
            $"C:1d2a01,{deviceConfig}",
            $"M:00,02,{metadata[..8]}",
            $"M:01,02,{metadata[8..]}",
            "X:something-to-ignore",
            $"L:{deviceList}",
            "C:ignored-after-L");

        var state = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello);

        state.IsComplete.Should().BeTrue();
        state.Header.Should().Be(Hello.RawBody);
        state.DeviceList.Should().Be(deviceList);
        state.Configurations.Should().HaveCount(2);
        state.GetDeviceConfiguration(0x1D2A3C).Should().Be(cubeConfig);
        state.GetDeviceConfiguration(0x1D2A01).Should().Be(deviceConfig);
        state.GetDeviceConfiguration(0x123456).Should().BeNull();
        state.MetadataCount.Should().Be(2);
        state.MetadataBlocks[0].Index.Should().Be(0);
        state.MetadataBlocks[1].Index.Should().Be(1);
        state.GetMetadataBase64().Should().Be(metadata);
        Encoding.UTF8.GetString(Convert.FromBase64String(state.GetMetadataBase64())).Should().Be("{\"rooms\":[]}");
        transport.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Repeated_metadata_index_replaces_the_previous_block()
    {
        var transport = new ScriptedCubeTransport(
            "M:00,01,AAAA", "M:00,01,BBBB", "L:" + B64(new byte[] { 6, 1, 2, 3, 0, 6 }));

        var state = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello);

        state.MetadataBlocks.Should().HaveCount(1);
        state.GetMetadataBase64().Should().Be("BBBB");
    }

    [Fact]
    public async Task Inconsistent_metadata_count_is_a_communication_failure()
    {
        var transport = new ScriptedCubeTransport("M:00,02,AAAA", "M:01,03,BBBB", "L:");

        var act = () => new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello).AsTask();

        var failure = await act.Should().ThrowAsync<CubeCommunicationFailedException>();
        failure.Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task Missing_metadata_block_is_reported_instead_of_being_silently_skipped()
    {
        var transport = new ScriptedCubeTransport("M:00,02,AAAA", "L:" + B64(new byte[] { 6, 1, 2, 3, 0, 6 }));
        var state = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello);

        state.IsComplete.Should().BeFalse();
        var act = () => state.GetMetadataBase64();
        act.Should().Throw<InvalidOperationException>().WithMessage("*block 1 of 2*");
    }

    [Theory]
    [InlineData("C:1d2a3c")]
    [InlineData("C:gg2a3c,YQ==")]
    [InlineData("C:ffffffff,YQ==")]
    [InlineData("M:00,02")]
    [InlineData("M:gg,02,YQ==")]
    [InlineData("M:00,00,YQ==")]
    [InlineData("M:00,17,YQ==")]
    [InlineData("M:02,02,YQ==")]
    public async Task Malformed_state_lines_are_rejected(string malformed)
    {
        var transport = new ScriptedCubeTransport(malformed, "L:" + B64(new byte[] { 6, 1, 2, 3, 0, 6 }));

        var act = () => new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello).AsTask();

        await act.Should().ThrowAsync<CubeCommunicationFailedException>();
    }

    [Fact]
    public async Task Oversized_metadata_block_is_rejected()
    {
        var options = new CubeConnectionOptions { MaxMetadataBlockBytes = 4 };
        var transport = new ScriptedCubeTransport("M:00,01,AAAAAAAA", "L:");

        var act = () => new StateReceiver(options).ReceiveAsync(transport, Hello).AsTask();

        await act.Should().ThrowAsync<CubeCommunicationFailedException>().WithMessage("*exceeding the limit*");
    }

    [Fact]
    public async Task End_of_stream_before_device_list_is_reported()
    {
        var transport = new ScriptedCubeTransport("C:1d2a3c,YQ==", null);

        var act = () => new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello).AsTask();

        await act.Should().ThrowAsync<CubeCommunicationFailedException>().WithMessage("*before the device list*");
    }

    [Fact]
    public async Task State_receive_timeout_is_bounded()
    {
        var options = new CubeConnectionOptions { StateReceiveTimeout = TimeSpan.FromMilliseconds(100) };
        await using var transport = new BlockingCubeTransport();

        var act = () => new StateReceiver(options).ReceiveAsync(transport, Hello).AsTask();

        var failure = await act.Should().ThrowAsync<CubeCommunicationFailedException>()
            .WithMessage("*Timed out waiting for the cube device list*");
        failure.Which.InnerException.Should().BeOfType<TimeoutException>();
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);
}
