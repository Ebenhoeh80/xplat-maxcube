using System.Globalization;
using System.Text;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;

namespace MaxCube.Protocol.State;

/// <summary>
/// Reads the initial state after the H: handshake until the L: line terminates the import.
/// Behaviour follows CubeConnection.receiveDeviceAndMetaData() (CubeConnection.java:157-183):
/// C: and M: lines are collected, all other prefixes are ignored, L: ends the loop.
/// </summary>
public sealed class StateReceiver
{
    private readonly CubeConnectionOptions _options;

    public StateReceiver(CubeConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <param name="transport">Connected transport; the H: line must already have been consumed.</param>
    /// <param name="hello">Parsed H: line, used to seed the RawCubeState header.</param>
    public async ValueTask<RawCubeState> ReceiveAsync(
        ICubeTransport transport,
        CubeHello hello,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(hello);

        var state = new RawCubeState { Hello = hello, Header = hello.RawBody };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.StateReceiveTimeout);

        try
        {
            while (true)
            {
                var line = await transport.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw new CubeCommunicationFailedException(
                        "Cube closed the connection before the device list (L:) arrived.");
                }

                if (line.StartsWith("L:", StringComparison.Ordinal))
                {
                    state.DeviceList = BodyAfterColon(line);
                    return state;
                }

                if (line.StartsWith("C:", StringComparison.Ordinal))
                {
                    ConsumeConfiguration(line, state);
                }
                else if (line.StartsWith("M:", StringComparison.Ordinal))
                {
                    ConsumeMetadata(line, state);
                }
                // All other prefixes are ignored during the initial import, exactly as
                // in the manufacturer client (CubeConnection.java:159-181).
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "Timed out waiting for the cube device list (L:).";
            throw new CubeCommunicationFailedException(message, new TimeoutException(message, exception));
        }
    }

    private static void ConsumeConfiguration(string line, RawCubeState state)
    {
        var arguments = Arguments(line);
        if (arguments.Length != 2)
        {
            throw new CubeCommunicationFailedException($"C: line has {arguments.Length} arguments instead of 2.");
        }

        if (!int.TryParse(arguments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)
            || address is < 0 or > 0xFFFFFF)
        {
            throw new CubeCommunicationFailedException($"C: radio address is not a 24-bit hex value: '{arguments[0]}'.");
        }

        state.SetDeviceConfiguration(address, arguments[1]);
    }

    private void ConsumeMetadata(string line, RawCubeState state)
    {
        var arguments = Arguments(line);
        if (arguments.Length != 3)
        {
            throw new CubeCommunicationFailedException($"M: line has {arguments.Length} arguments instead of 3.");
        }

        if (!int.TryParse(arguments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var index)
            || !int.TryParse(arguments[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var count))
        {
            throw new CubeCommunicationFailedException("M: index/count are not hexadecimal.");
        }

        if (count <= 0 || count > 16)
        {
            throw new CubeCommunicationFailedException($"M: block count {count} is outside 1..16 (Metadata.java:10).");
        }

        if (index < 0 || index >= count)
        {
            throw new CubeCommunicationFailedException($"M: block index {index} is outside 0..{count - 1}.");
        }

        var payload = Encoding.ASCII.GetBytes(arguments[2]);
        if (payload.Length > _options.MaxMetadataBlockBytes)
        {
            throw new CubeCommunicationFailedException(
                $"M: block {index} has {payload.Length} bytes, exceeding the limit of {_options.MaxMetadataBlockBytes}.");
        }

        try
        {
            state.SetMetadataBlock(index, count, payload);
        }
        catch (ArgumentException exception)
        {
            throw new CubeCommunicationFailedException($"Inconsistent M: block: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Java uses StringTokenizer(command, ":") for L: and substring(2).split(",") for C:/M:
    /// (CubeConnection.java:199-213). Base64 contains '=' but never ':'.
    /// </summary>
    private static string BodyAfterColon(string line)
    {
        var separator = line.IndexOf(':');
        return separator >= 0 && separator + 1 < line.Length ? line[(separator + 1)..] : string.Empty;
    }

    private static string[] Arguments(string line)
    {
        if (line.Length <= 2)
        {
            return Array.Empty<string>();
        }

        return line[2..].Split(',');
    }
}
