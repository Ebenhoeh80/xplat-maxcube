using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class TcpCubeTransportTests
{
    [Fact]
    public async Task Reads_hello_from_loopback_socket_and_writes_crlf_command()
    {
        var listener = StartListener(out var port);
        var server = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes("H:JEQ0001234,1d2a3c,111\r\n"));
                var command = await ReadExactlyAsync(stream, 4);
                return Encoding.ASCII.GetString(command);
            }
            catch (Exception exception) when (IsServerShutdown(exception))
            {
                return string.Empty;
            }
        });

        try
        {
            await using var transport = new TcpCubeTransport(new CubeConnectionOptions { Port = port });
            await transport.ConnectAsync();
            transport.IsConnected.Should().BeTrue();

            (await transport.ReadLineAsync()).Should().Be("H:JEQ0001234,1d2a3c,111");
            await transport.WriteLineAsync("q:");

            (await server).Should().Be("q:\r\n");
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    [Fact]
    public async Task Connect_to_closed_port_throws_cube_transport_exception()
    {
        var listener = StartListener(out var port);
        listener.Stop();

        await using var transport = new TcpCubeTransport(new CubeConnectionOptions { Port = port });

        var act = () => transport.ConnectAsync().AsTask();

        await act.Should().ThrowAsync<CubeTransportException>()
            .WithMessage($"Cannot connect to cube at 127.0.0.1:{port}.");
    }

    [Fact]
    public async Task Reading_or_writing_before_connect_is_rejected()
    {
        await using var transport = new TcpCubeTransport(new CubeConnectionOptions());

        var read = () => transport.ReadLineAsync().AsTask();
        var write = () => transport.WriteLineAsync("q:").AsTask();

        await read.Should().ThrowAsync<CubeTransportException>().WithMessage("*not connected*");
        await write.Should().ThrowAsync<CubeTransportException>().WithMessage("*not connected*");
    }

    [Fact]
    public async Task Second_connect_is_rejected()
    {
        var listener = StartListener(out var port);
        var server = HoldConnectionAsync(listener);

        try
        {
            await using var transport = new TcpCubeTransport(new CubeConnectionOptions { Port = port });
            await transport.ConnectAsync();

            var act = () => transport.ConnectAsync().AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already connected*");
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    [Fact]
    public async Task Caller_cancellation_faults_the_transport()
    {
        var listener = StartListener(out var port);
        var server = HoldConnectionAsync(listener);

        try
        {
            await using var transport = new TcpCubeTransport(new CubeConnectionOptions { Port = port });
            await transport.ConnectAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(100);

            var act = () => transport.ReadLineAsync(cancellation.Token).AsTask();

            await act.Should().ThrowAsync<OperationCanceledException>();
            transport.IsConnected.Should().BeFalse();
            await act.Should().ThrowAsync<CubeTransportException>().WithMessage("*faulted*");
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    [Fact]
    public async Task Socket_timeout_faults_the_transport_and_wraps_timeout_exception()
    {
        var listener = StartListener(out var port);
        var server = HoldConnectionAsync(listener);
        var options = new CubeConnectionOptions { Port = port, SocketTimeout = TimeSpan.FromMilliseconds(200) };

        try
        {
            await using var transport = new TcpCubeTransport(options);
            await transport.ConnectAsync();

            var act = () => transport.ReadLineAsync().AsTask();

            var failure = await act.Should().ThrowAsync<CubeTransportException>()
                .WithMessage("Timed out reading from the cube.");
            failure.Which.InnerException.Should().BeOfType<TimeoutException>();
            transport.IsConnected.Should().BeFalse();
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    [Fact]
    public async Task Line_too_long_faults_the_transport()
    {
        var listener = StartListener(out var port);
        var server = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(new string('x', 50)));
            }
            catch (Exception exception) when (IsServerShutdown(exception))
            {
                // expected when the client closes the faulted connection
            }
        });
        var options = new CubeConnectionOptions { Port = port, MaxLineLength = 8 };

        try
        {
            await using var transport = new TcpCubeTransport(options);
            await transport.ConnectAsync();

            var act = () => transport.ReadLineAsync().AsTask();

            await act.Should().ThrowAsync<LineTooLongException>();
            transport.IsConnected.Should().BeFalse();
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    [Fact]
    public async Task Read_after_dispose_is_rejected()
    {
        var listener = StartListener(out var port);
        var server = HoldConnectionAsync(listener);

        var transport = new TcpCubeTransport(new CubeConnectionOptions { Port = port });
        try
        {
            await transport.ConnectAsync();
            await transport.DisposeAsync();
            transport.IsConnected.Should().BeFalse();

            var act = () => transport.ReadLineAsync().AsTask();
            await act.Should().ThrowAsync<CubeTransportException>().WithMessage("*not connected*");
        }
        finally
        {
            await StopAsync(listener, server);
        }
    }

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static Task HoldConnectionAsync(TcpListener listener) => Task.Run(async () =>
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await Task.Delay(1000);
        }
        catch (Exception exception) when (IsServerShutdown(exception))
        {
            // expected during cleanup
        }
    });

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return buffer[..offset];
    }

    private static bool IsServerShutdown(Exception exception)
        => exception is SocketException or ObjectDisposedException or IOException;

    private static async Task StopAsync(TcpListener listener, Task server)
    {
        listener.Stop();
        try
        {
            await server;
        }
        catch
        {
            // cleanup must never hide the original test failure
        }
    }
}
