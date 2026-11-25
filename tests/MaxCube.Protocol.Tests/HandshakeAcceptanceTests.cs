using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using Xunit;

namespace MaxCube.Protocol.Tests;

/// <summary>End-to-end acceptance tests for AP-1.1 without sockets or hardware.</summary>
public class HandshakeAcceptanceTests
{
    [Fact]
    public async Task Golden_hello_from_memory_stream_is_accepted()
    {
        var capture = Encoding.ASCII.GetBytes("H:001234567890,12345678,0111\r\nL:00\r\n");
        await using var stream = new MemoryStream(capture);
        await using var transport = new StreamCubeTransport(stream);
        await transport.ConnectAsync();
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var hello = await handshaker.PerformAsync(transport);

        hello.SerialNumber.Should().Be("001234567890");
        hello.RadioAddress.Should().Be(0x12345678);
        hello.FirmwareVersion.Should().Be(273);
        (await transport.ReadLineAsync()).Should().Be("L:00");
    }

    [Theory]
    [InlineData("H:001234567890,12345678,0111")] // 273
    [InlineData("H:001234567890,12345678,0112")] // 274
    [InlineData("H:001234567890,12345678,0113")] // 275
    public async Task Accepts_all_supported_firmware_values_through_the_handshake(string line)
    {
        await using var transport = new StreamCubeTransport(new MemoryStream(Encoding.ASCII.GetBytes(line + "\r\n")));
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var hello = await handshaker.PerformAsync(transport);

        hello.FirmwareVersion.Should().BeGreaterThanOrEqualTo(273);
        hello.FirmwareVersion.Should().BeLessThanOrEqualTo(275);
    }

    [Theory]
    [InlineData("0110", 272)] // one below the window
    [InlineData("0114", 276)] // one above the window
    public async Task Rejects_firmware_outside_the_window_through_the_handshake(string hex, int expected)
    {
        await using var transport = new StreamCubeTransport(
            new MemoryStream(Encoding.ASCII.GetBytes($"H:001234567890,12345678,{hex}\r\n")));
        var handshaker = new CubeHandshaker(new CubeConnectionOptions());

        var act = () => handshaker.PerformAsync(transport);

        var failure = await act.Should().ThrowAsync<IncompatibleFirmwareException>();
        failure.Which.FirmwareVersion.Should().Be(expected);
        failure.Which.Minimum.Should().Be(273);
        failure.Which.Maximum.Should().Be(275);
    }

    [Fact]
    public async Task Hello_timeout_is_wrapped_as_cube_transport_exception()
    {
        var options = new CubeConnectionOptions { HelloTimeout = TimeSpan.FromMilliseconds(100) };
        await using var transport = new BlockingCubeTransport();

        var act = () => new CubeHandshaker(options).PerformAsync(transport);

        var failure = await act.Should().ThrowAsync<CubeTransportException>()
            .WithMessage("Timed out waiting for the cube H: hello.");
        failure.Which.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public void All_handshake_failures_are_cube_communication_failures()
    {
        typeof(NoIncomingHelloException).Should().BeDerivedFrom<CubeCommunicationFailedException>();
        typeof(MalformedHelloException).Should().BeDerivedFrom<CubeCommunicationFailedException>();
        typeof(IncompatibleFirmwareException).Should().BeDerivedFrom<CubeCommunicationFailedException>();
    }

    [Fact]
    public void Hello_to_string_redacts_serial_number_and_raw_body()
    {
        var hello = HelloParser.Parse("H:JEQ0001234,1d2a3c,111");

        var text = hello.ToString();

        text.Should().Contain("Serial=***");
        text.Should().NotContain("JEQ0001234");
        text.Should().NotContain("1d2a3c,111");
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        var invalidPort = () => new CubeHandshaker(new CubeConnectionOptions { Port = 0 });
        var invalidRange = () => new CubeHandshaker(new CubeConnectionOptions
        {
            MinimumFirmwareVersion = 300,
            MaximumFirmwareVersion = 275,
        });
        var hugeTimeout = () => new CubeHandshaker(new CubeConnectionOptions
        {
            SocketTimeout = TimeSpan.FromDays(30),
        });
        var invalidLineLimit = () => new CubeHandshaker(new CubeConnectionOptions { MaxLineLength = 0 });

        invalidPort.Should().Throw<ArgumentOutOfRangeException>();
        invalidRange.Should().Throw<ArgumentException>();
        hugeTimeout.Should().Throw<ArgumentException>().WithMessage("*24.9 days*");
        invalidLineLimit.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Pre_cancelled_connect_is_not_wrapped_as_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var transport = new StreamCubeTransport(new MemoryStream());

        var act = () => transport.ConnectAsync(cancellation.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
