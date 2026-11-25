using System.Net.Sockets;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Framing;

namespace MaxCube.Protocol;

/// <summary>TCP transport for the physical cube (port 62910) using a CRLF line protocol.</summary>
public sealed class TcpCubeTransport : ICubeTransport
{
    private readonly CubeConnectionOptions _options;
    private TcpClient? _client;
    private LineReader? _reader;
    private bool _faulted;

    public TcpCubeTransport(CubeConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public bool IsConnected => !_faulted && _client?.Connected == true;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_faulted)
        {
            throw new CubeTransportException("Cube transport is faulted. Create a new transport to reconnect.");
        }

        if (_client is not null)
        {
            throw new InvalidOperationException("The cube transport is already connected. Dispose it before reconnecting.");
        }

        var client = new TcpClient();
        var connected = false;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.ConnectTimeout);
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, timeoutSource.Token).ConfigureAwait(false);
            client.NoDelay = true;
            _reader = new LineReader(client.GetStream(), maxLineLength: _options.MaxLineLength);
            _client = client;
            connected = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            const string message = "Timed out connecting to the cube.";
            throw new CubeTransportException(message, new TimeoutException(message, exception));
        }
        catch (Exception exception)
        {
            throw new CubeTransportException(
                $"Cannot connect to cube at {_options.Host}:{_options.Port}.", exception);
        }
        finally
        {
            if (!connected)
            {
                client.Dispose();
            }
        }
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => ReadLineAsync(null, cancellationToken);

    public async ValueTask<string?> ReadLineAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        try
        {
            return await WithTimeoutAsync(
                ct => reader.ReadLineAsync(ct),
                "Timed out reading from the cube.",
                "Reading from the cube was cancelled.",
                "Cannot read from the cube.",
                cancellationToken,
                timeout).ConfigureAwait(false);
        }
        catch (LineTooLongException)
        {
            MarkFaulted();
            throw;
        }
    }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        => WriteLineAsync(line, null, cancellationToken);

    public async ValueTask WriteLineAsync(string line, TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        // A malformed command (embedded CR/LF) is a programming error from LineReader;
        // it does not fault the connection.
        await WithTimeoutAsync(
            ct => reader.WriteLineAsync(line, ct),
            "Timed out writing to the cube.",
            "Writing to the cube was cancelled.",
            "Cannot write to the cube.",
            cancellationToken,
            timeout).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        var client = Interlocked.Exchange(ref _client, null);
        _reader = null;
        client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<T> WithTimeoutAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        string timeoutMessage,
        string cancelMessage,
        string errorMessage,
        CancellationToken callerToken,
        TimeSpan? timeout = null)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource.CancelAfter(timeout ?? _options.SocketTimeout);
        try
        {
            return await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (callerToken.IsCancellationRequested)
        {
            // A cancelled read/write may leave a partial command on the wire: the
            // original closes the socket on timeout (CubeConnection.java:236-241).
            MarkFaulted();
            throw new OperationCanceledException(cancelMessage, exception, callerToken);
        }
        catch (OperationCanceledException exception)
        {
            throw Fault(timeoutMessage, new TimeoutException(timeoutMessage, exception));
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            throw Fault(errorMessage, exception);
        }
    }

    private async ValueTask WithTimeoutAsync(
        Func<CancellationToken, ValueTask> operation,
        string timeoutMessage,
        string cancelMessage,
        string errorMessage,
        CancellationToken callerToken,
        TimeSpan? timeout = null)
    {
        await WithTimeoutAsync(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            timeoutMessage,
            cancelMessage,
            errorMessage,
            callerToken,
            timeout).ConfigureAwait(false);
    }

    private CubeTransportException Fault(string message, Exception innerException)
    {
        MarkFaulted();
        return new CubeTransportException(message, innerException);
    }

    private void MarkFaulted()
    {
        _faulted = true;
        var client = Interlocked.Exchange(ref _client, null);
        _reader = null;
        client?.Dispose();
    }

    private LineReader GetReader()
    {
        if (_faulted)
        {
            throw new CubeTransportException("Cube transport is faulted. Create a new transport to reconnect.");
        }

        if (_client is null || _reader is null || !_client.Connected)
        {
            throw new CubeTransportException(
                "Cube transport is not connected. Call ConnectAsync before reading or writing.");
        }

        return _reader;
    }
}
