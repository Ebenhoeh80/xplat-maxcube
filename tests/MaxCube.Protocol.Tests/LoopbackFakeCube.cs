using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MaxCube.Protocol.Tests;

/// <summary>
/// Deterministic fake cube on a loopback socket. Wire lines follow the documented sequence
/// (docs/maxcube-protocol.md:606-631) and the Python fake-cube behaviour in ha-maxcube
/// (tests/fake_cube.py:360-394): H:, C:, M:, L: on connect, then A:/S:/L: replies.
/// This is a test harness, not a hardware golden capture.
/// </summary>
internal sealed class LoopbackFakeCube : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _server;

    public LoopbackFakeCube()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _server = Task.Run(ServeAsync);
    }

    public int Port { get; }

    public List<string> ReceivedCommands { get; } = new();

    private async Task ServeAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            await SendAsync(stream,
                "H:JEQ0001234,1d2a3c,0113,00,00,05,1f,1a0b18,0c1e,3,0002",
                $"C:1d2a3c,{Convert.ToBase64String(new byte[] { 1, 0, 0x1d, 0x2a, 0x3c, 2, 1 })}",
                $"M:00,01,{Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"rooms\":[]}"))}",
                $"L:{Convert.ToBase64String(new byte[] { 6, 0x1d, 0x2a, 0x01, 0, 0x06 })}");

            var buffer = new byte[4096];
            var pending = new StringBuilder();
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                pending.Append(Encoding.ASCII.GetString(buffer, 0, read));
                while (true)
                {
                    var terminator = FindTerminator(pending);
                    if (terminator < 0)
                    {
                        break;
                    }

                    var line = pending.ToString(0, terminator);
                    pending.Remove(0, terminator + 2);
                    lock (ReceivedCommands)
                    {
                        ReceivedCommands.Add(line);
                    }

                    if (line == "q:")
                    {
                        return;
                    }

                    var reply = line switch
                    {
                        "l:" => $"L:{Convert.ToBase64String(new byte[] { 6, 0x1d, 0x2a, 0x01, 0, 0x06 })}",
                        _ when line.StartsWith("s:", StringComparison.Ordinal) => "S:05,0,1f",
                        _ => "A:",
                    };
                    await SendAsync(stream, reply);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            // normal teardown
        }
    }

    private static int FindTerminator(StringBuilder pending)
    {
        for (var i = 0; i < pending.Length - 1; i++)
        {
            if (pending[i] == '\r' && pending[i + 1] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static async Task SendAsync(NetworkStream stream, params string[] lines)
    {
        var text = string.Concat(lines.Select(line => line + "\r\n"));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _server;
        }
        catch
        {
            // cleanup must not hide the original test failure
        }
    }
}
