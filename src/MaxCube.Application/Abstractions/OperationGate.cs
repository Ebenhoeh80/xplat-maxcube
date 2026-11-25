namespace MaxCube.Application.Abstractions;

/// <summary>
/// Serialisiert Operationen, ohne dass ein hängender Transport den Aufrufer festhält.
/// Der Gate-Erwerb ist zeitbegrenzt; <see cref="CancelActive"/> bricht die gerade laufende
/// Operation über ihren CancellationToken ab (Gremium Runde 1, N7). App und Gateway
/// besitzen je eine eigene Instanz, damit ein blockiertes Gateway-Gate das App-Gate
/// nicht mitblockiert.
/// </summary>
public sealed class OperationGate
{
    /// <summary>Obergrenze für das Warten auf den Gate-Erwerb.</summary>
    public static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Kürzere Grenze für den Shutdown-Pfad.</summary>
    public static readonly TimeSpan ShutdownAcquireTimeout = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _active;

    /// <summary>
    /// Erwirbt das Gate. Die Lease trägt den gelinkten Token, der sowohl beim
    /// Abbrechen durch die UI als auch beim Dispose der Lease ungültig wird.
    /// </summary>
    public async Task<OperationLease> EnterAsync(
        CancellationToken callerToken = default,
        TimeSpan? acquireTimeout = null)
    {
        using var acquire = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        acquire.CancelAfter(acquireTimeout ?? AcquireTimeout);
        await _semaphore.WaitAsync(acquire.Token).ConfigureAwait(false);

        var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        lock (_sync)
        {
            _active?.Dispose();
            _active = linked;
        }

        return new OperationLease(this, linked);
    }

    /// <summary>Bricht die aktive Operation ab, ohne auf das Gate zu warten.</summary>
    public void CancelActive()
    {
        try
        {
            lock (_sync)
            {
                _active?.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Die Operation ist inzwischen fertig geworden.
        }
    }

    internal void Release(OperationLease lease, CancellationTokenSource linked)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_active, linked))
            {
                _active = null;
            }
        }

        _semaphore.Release();
        linked.Dispose();
    }
}

/// <summary>Gate-Lease; muss immer mit <c>using</c> freigegeben werden.</summary>
public sealed class OperationLease : IDisposable
{
    private readonly OperationGate _owner;
    private readonly CancellationTokenSource _linked;
    private int _disposed;

    internal OperationLease(OperationGate owner, CancellationTokenSource linked)
    {
        _owner = owner;
        _linked = linked;
    }

    /// <summary>Token der aktiven Operation (Abbrechen/Caller/Shutdown).</summary>
    public CancellationToken Token => _linked.Token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _owner.Release(this, _linked);
    }
}
