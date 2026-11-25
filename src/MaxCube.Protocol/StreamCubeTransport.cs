using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Framing;

namespace MaxCube.Protocol;

/// <summary>
/// Transport over an already opened stream. Used for golden-capture replay and unit tests
/// (MemoryStream); no socket and no hardware required.
/// </summary>
public sealed class StreamCubeTransport : ICubeTransport
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly LineReader _reader;

    public StreamCubeTransport(Stream stream, bool leaveOpen = false, int maxLineLength = 65536)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
        _reader = new LineReader(stream, maxLineLength: maxLineLength);
    }

    public bool IsConnected { get; private set; }

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "Timed out reading from the cube.";
            throw new CubeTransportException(message, new TimeoutException(message, exception));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new CubeTransportException("Cannot read from the cube.", exception);
        }
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        try
        {
            await _reader.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "Timed out writing to the cube.";
            throw new CubeTransportException(message, new TimeoutException(message, exception));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new CubeTransportException("Cannot write to the cube.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
