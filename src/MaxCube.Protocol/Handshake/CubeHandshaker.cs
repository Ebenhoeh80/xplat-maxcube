using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.Handshake;

/// <summary>
/// Performs steps 3-6 of CubeConnection.java:53-62: read the first line,
/// require H:, parse it and validate the firmware before the connection is
/// considered ready. No socket, no hardware, no UI dependency.
/// </summary>
public sealed class CubeHandshaker
{
    private readonly CubeConnectionOptions _options;

    public CubeHandshaker(CubeConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<CubeHello> PerformAsync(ICubeTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        CubeHello hello;
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            // Original: setSoTimeout(connectTimeout) before the first readLine (CubeConnection.java:48,53).
            timeoutSource.CancelAfter(_options.HelloTimeout);
            string? line;
            try
            {
                line = await transport.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                const string message = "Timed out waiting for the cube H: hello.";
                throw new CubeTransportException(message, new TimeoutException(message, exception));
            }

            hello = HelloParser.Parse(line, _options.TimeProvider);
        }

        FirmwareGuard.EnsureSupported(hello.FirmwareVersion, _options);
        return hello;
    }
}
