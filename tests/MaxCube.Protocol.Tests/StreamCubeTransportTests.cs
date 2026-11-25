using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class StreamCubeTransportTests
{
    [Fact]
    public async Task Connect_sets_isconnected_and_reads_replayed_golden_lines()
    {
        var capture = Encoding.ASCII.GetBytes("H:JEQ0001234,1d2a3c,111\r\nL:00\r\n");
        await using var stream = new MemoryStream(capture);
        await using var transport = new StreamCubeTransport(stream);

        transport.IsConnected.Should().BeFalse();
        await transport.ConnectAsync();
        transport.IsConnected.Should().BeTrue();

        (await transport.ReadLineAsync()).Should().Be("H:JEQ0001234,1d2a3c,111");
        (await transport.ReadLineAsync()).Should().Be("L:00");
        (await transport.ReadLineAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Write_line_is_visible_in_the_stream()
    {
        await using var stream = new MemoryStream();
        await using var transport = new StreamCubeTransport(stream, leaveOpen: true);

        await transport.WriteLineAsync("l:");

        Encoding.ASCII.GetString(stream.ToArray()).Should().Be("l:\r\n");
    }

    [Fact]
    public async Task Read_io_error_is_wrapped_as_cube_transport_exception()
    {
        await using var transport = new StreamCubeTransport(new ThrowingStream());
        await transport.ConnectAsync();

        var act = () => transport.ReadLineAsync().AsTask();

        var failure = await act.Should().ThrowAsync<CubeTransportException>()
            .WithMessage("Cannot read from the cube.");
        failure.Which.InnerException.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task Write_io_error_is_wrapped_as_cube_transport_exception()
    {
        await using var transport = new StreamCubeTransport(new ThrowingStream());
        await transport.ConnectAsync();

        var act = () => transport.WriteLineAsync("q:").AsTask();

        var failure = await act.Should().ThrowAsync<CubeTransportException>()
            .WithMessage("Cannot write to the cube.");
        failure.Which.InnerException.Should().BeOfType<IOException>();
    }
}
