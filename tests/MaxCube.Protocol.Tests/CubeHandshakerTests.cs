using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class CubeHandshakerTests
{
    [Fact]
    public async Task Reads_parses_and_validates_the_first_line()
    {
        var transport = new ScriptedCubeTransport("H:001234567890,12345678,0111");
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var hello = await handshaker.PerformAsync(transport);

        hello.SerialNumber.Should().Be("001234567890");
        hello.RadioAddress.Should().Be(0x12345678);
        hello.FirmwareVersion.Should().Be(273);
    }

    [Fact]
    public async Task Rejects_a_state_line_as_first_line()
    {
        var transport = new ScriptedCubeTransport("L:00");
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var act = () => handshaker.PerformAsync(transport);

        await act.Should().ThrowAsync<NoIncomingHelloException>();
    }

    [Fact]
    public async Task Rejects_eof_before_hello()
    {
        var transport = new ScriptedCubeTransport();
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var act = () => handshaker.PerformAsync(transport);

        await act.Should().ThrowAsync<NoIncomingHelloException>();
    }

    [Fact]
    public async Task Rejects_unsupported_firmware_before_marking_ready()
    {
        var transport = new ScriptedCubeTransport("H:JEQ0001234,1d2a3c,0114");
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var act = () => handshaker.PerformAsync(transport);

        await act.Should().ThrowAsync<IncompatibleFirmwareException>();
    }
}
