namespace MaxCube.Protocol.Session;

public enum CubeSessionState
{
    Active,
    Idle,
    KeepAliveDue,
    Unreachable,
}

/// <summary>
/// Deterministic idle/watchdog logic for one cube connection. Uses the options'
/// TimeProvider, so tests can drive it with a fake clock instead of real delays
/// (roadmap.md AP-1.3 acceptance criterion 4).
/// </summary>
public sealed class CubeSessionMonitor
{
    private readonly CubeConnectionOptions _options;
    private DateTimeOffset _lastActivity;

    public CubeSessionMonitor(CubeConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _lastActivity = _options.TimeProvider.GetUtcNow();
    }

    public DateTimeOffset LastActivity => _lastActivity;

    public void RecordActivity() => _lastActivity = _options.TimeProvider.GetUtcNow();

    public CubeSessionState Evaluate(DateTimeOffset now)
    {
        var elapsed = now - _lastActivity;
        if (elapsed >= _options.UnreachableTimeout)
        {
            return CubeSessionState.Unreachable;
        }

        if (elapsed >= _options.KeepAliveInterval)
        {
            return CubeSessionState.KeepAliveDue;
        }

        if (elapsed >= _options.IdleTimeout)
        {
            return CubeSessionState.Idle;
        }

        return CubeSessionState.Active;
    }

    /// <summary>
    /// Periodic watchdog. onWatchdog runs while idle; onKeepAlive runs once the keepalive
    /// interval elapsed. Callbacks call <see cref="RecordActivity"/> when they actually
    /// exchanged data with the cube.
    /// </summary>
    public async Task RunAsync(
        Func<CancellationToken, Task> onWatchdog,
        Func<CancellationToken, Task> onKeepAlive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onWatchdog);
        ArgumentNullException.ThrowIfNull(onKeepAlive);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_options.WatchdogInterval, _options.TimeProvider, cancellationToken).ConfigureAwait(false);
            switch (Evaluate(_options.TimeProvider.GetUtcNow()))
            {
                case CubeSessionState.KeepAliveDue:
                    await onKeepAlive(cancellationToken).ConfigureAwait(false);
                    break;
                case CubeSessionState.Idle:
                    await onWatchdog(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }
}
