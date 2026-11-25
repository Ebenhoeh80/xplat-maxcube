using System.Threading.Channels;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol;

/// <summary>
/// Single-writer command queue for one cube connection. Mirrors the manufacturer design:
/// ArrayBlockingQueue(100), one worker, synchronous result per command, purge on connection
/// errors (CubeConnection.java:36,139-155,185-197,249-261).
/// The queue owns the transport after <see cref="Start"/> and disposes it on
/// <see cref="StopAsync"/>/<see cref="DisposeAsync"/>; a fault closes the command channel and
/// rejects all pending commands.
/// </summary>
public sealed class CubeCommandQueue : IAsyncDisposable
{
    private readonly ICubeTransport _transport;
    private readonly CubeConnectionOptions _options;
    private readonly Channel<Envelope> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _worker;
    private bool _faulted;
    private bool _inclusionMode;

    public CubeCommandQueue(ICubeTransport transport, CubeConnectionOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _channel = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(_options.CommandQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public bool IsFaulted { get { lock (_gate) { return _faulted; } } }

    /// <summary>Number of commands waiting to be processed (diagnostics and deterministic tests).</summary>
    public int QueuedCount => _channel.Reader.Count;

    public bool IsInclusionMode { get { lock (_gate) { return _inclusionMode; } } }

    /// <summary>Timestamp of the last successful command; drives the session watchdog.</summary>
    public DateTimeOffset LastActivity { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_faulted)
            {
                throw new CubeTransportException("The cube command queue is faulted.");
            }

            if (_worker is not null)
            {
                throw new InvalidOperationException("The cube command queue is already started.");
            }

            LastActivity = _options.TimeProvider.GetUtcNow();
            _worker = Task.Run(() => RunAsync(_stop.Token));
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(CubeCommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Envelope envelope;
        lock (_gate)
        {
            if (_faulted || _stop.IsCancellationRequested)
            {
                throw new CubeTransportException("The cube command queue is not available.");
            }

            if (_worker is null)
            {
                throw new InvalidOperationException("Call Start() before executing cube commands.");
            }

            if (_inclusionMode && !command.AllowedDuringInclusion)
            {
                throw new CubeIsInInclusionModeException();
            }

            envelope = new Envelope(command);
        }

        try
        {
            if (!await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CubeTransportException("The cube command queue was closed.");
            }

            await _channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            envelope.Cancel(cancellationToken);
            throw new CubeTransportException("The cube command queue was closed.", exception);
        }

        // Fault() may have run between the gate check and the write; in that case this
        // envelope was written after the purge and must be rejected explicitly.
        lock (_gate)
        {
            if (_faulted)
            {
                Purge(new CubeCommunicationFailedException("command ignored: queue faulted"));
            }
        }

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() => envelope.Cancel(cancellationToken))
            : default;

        return await command.Result.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        _channel.Writer.TryComplete();
        _stop.Cancel();
        var worker = _worker;
        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }
        }

        Purge(new CubeCommunicationFailedException("command ignored: queue stopped"));
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken stopToken)
    {
        try
        {
            await foreach (var envelope in _channel.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                if (envelope.IsCanceled)
                {
                    continue;
                }

                await ProcessAsync(envelope, stopToken).ConfigureAwait(false);
                if (IsFaulted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }

    private async Task ProcessAsync(Envelope envelope, CancellationToken stopToken)
    {
        var command = envelope.Command;
        var effectiveTimeout = command.Timeout ?? _options.CommandTimeout;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        timeout.CancelAfter(effectiveTimeout);
        try
        {
            // The guard must also run at execution time: commands enqueued before n:
            // may otherwise be sent while the cube is in inclusion mode
            // (CubeConnection.java:232-235,264-268). A rejection here is not a fault.
            lock (_gate)
            {
                if (_inclusionMode && !command.AllowedDuringInclusion)
                {
                    ((IPendingCommand)command).Fail(new CubeIsInInclusionModeException());
                    return;
                }
            }

            await _transport.WriteLineAsync(command.WireLine, effectiveTimeout, timeout.Token).ConfigureAwait(false);
            string? response = null;
            if (command.ExpectsResponse)
            {
                if (command.ResponseMode == ResponseReadMode.UntilEmptyBody)
                {
                    // Java ignores lines without the expected prefix while waiting for
                    // the empty N: body (CancelInclusionModeCubeCommand.java:37-49).
                    var responses = new List<string>();
                    for (var read = 0; read < command.MaxResponses; read++)
                    {
                        var next = await _transport.ReadLineAsync(effectiveTimeout, timeout.Token).ConfigureAwait(false);
                        if (next is null)
                        {
                            throw new CubeCommunicationFailedException("Cube closed the connection during command responses.");
                        }

                        if (command.ResponsePrefix is not null
                            && !next.StartsWith(command.ResponsePrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        responses.Add(next);
                        if (HasEmptyBody(next))
                        {
                            break;
                        }
                    }

                    if (responses.Count == 0 || !HasEmptyBody(responses[^1]))
                    {
                        throw new CubeCommunicationFailedException(
                            $"No empty response line after {command.MaxResponses} reads; the stream is desynchronized.");
                    }

                    response = string.Join('\n', responses);
                }
                else
                {
                    response = await _transport.ReadLineAsync(effectiveTimeout, timeout.Token).ConfigureAwait(false);
                    if (response is null)
                    {
                        throw new CubeCommunicationFailedException("Cube closed the connection before the command response.");
                    }
                }
            }

            // Set the mode before completing the TCS; an awaiter may immediately enqueue
            // the next command (CubeConnection.java:45-49 sets it right after writing).
            var effect = command.ResolveInclusionModeEffect(response);
            if (effect != InclusionModeEffect.None)
            {
                lock (_gate)
                {
                    _inclusionMode = effect == InclusionModeEffect.Enter;
                }
            }

            try
            {
                ((IPendingCommand)command).Complete(response);
            }
            catch (Exception parseException)
            {
                // A bad response prefix rejects this command only. The line was already
                // consumed, so the stream is still in sync (CubeConnection.java:236-246).
                ((IPendingCommand)command).Fail(parseException);
            }

            LastActivity = _options.TimeProvider.GetUtcNow();
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !stopToken.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException("Timed out waiting for the cube command response.", exception);
            var failure = new CubeCommunicationFailedException(timeoutException.Message, timeoutException);
            // Erst den Queue-Status setzen, dann den Waiter freigeben: die TCS läuft mit
            // RunContinuationsAsynchronously, sonst könnte ein Aufrufer IsFaulted=false sehen.
            Fault(failure);
            ((IPendingCommand)command).Fail(failure);
        }
        catch (OperationCanceledException exception) when (stopToken.IsCancellationRequested || envelope.IsCanceled)
        {
            ((IPendingCommand)command).Cancel(stopToken.IsCancellationRequested ? stopToken : exception.CancellationToken);
        }
        catch (Exception exception)
        {
            Fault(exception);
            ((IPendingCommand)command).Fail(exception);
        }
    }

    private static bool HasEmptyBody(string line)
    {
        var separator = line.IndexOf(':');
        return separator < 0 || separator == line.Length - 1;
    }

    private void Fault(Exception cause)
    {
        lock (_gate)
        {
            if (_faulted)
            {
                return;
            }

            _faulted = true;
        }

        _channel.Writer.TryComplete();
        Purge(new CubeCommunicationFailedException("command ignored", cause));
    }

    /// <summary>purgeCommandQueue() (CubeConnection.java:249-261): reject every waiter.</summary>
    private void Purge(Exception cause)
    {
        while (_channel.Reader.TryRead(out var pending))
        {
            ((IPendingCommand)pending.Command).Fail(cause);
        }
    }

    private sealed class Envelope
    {
        public Envelope(ICubeCommand command) => Command = command;

        public ICubeCommand Command { get; }

        public bool IsCanceled { get; private set; }

        public void Cancel(CancellationToken token)
        {
            if (IsCanceled)
            {
                return;
            }

            IsCanceled = true;
            ((IPendingCommand)Command).Cancel(token);
        }
    }
}
