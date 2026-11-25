namespace MaxCube.Protocol;

/// <summary>
/// Connection parameters. Defaults are taken from the manufacturer properties
/// (max.local.properties:17-24): ports 62910 / 54321, 30 s connect, 70 s socket,
/// firmware window 273..275.
/// </summary>
public sealed record CubeConnectionOptions
{
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>Physical cube TCP port. 54321 is only the manufacturer's virtual cube.</summary>
    public int Port { get; init; } = 62910;

    /// <summary>
    /// C#-specific connect timeout. The Java original uses the OS default for
    /// new Socket(ip, port) (CubeConnection.java:46); its 30 s value is a read
    /// timeout for the H: line, represented by HelloTimeout below.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Socket timeout for the first read, i.e. the H: line
    /// (setSoTimeout before readLine, CubeConnection.java:48,53).
    /// </summary>
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Socket timeout after the handshake (CubeConnection.java:60).</summary>
    public TimeSpan SocketTimeout { get; init; } = TimeSpan.FromSeconds(70);

    /// <summary>max.leastCompatibleCubeFirmwareVersion (max.local.properties:17).</summary>
    public int MinimumFirmwareVersion { get; init; } = 273;

    /// <summary>max.latestCubeFirmwareVersion (max.local.properties:18).</summary>
    public int MaximumFirmwareVersion { get; init; } = 275;

    /// <summary>Limit for a single protocol line; larger M:/L: blocks are not expected.</summary>
    public int MaxLineLength { get; init; } = 65536;

    /// <summary>
    /// Overall deadline for the initial C:/M:/L: state import. The Java client uses the
    /// 70 s socket read timeout; this deadline additionally bounds many small lines.
    /// </summary>
    public TimeSpan StateReceiveTimeout { get; init; } = TimeSpan.FromSeconds(70);

    /// <summary>Maximum ASCII payload bytes of one M: block (1900 raw bytes = 2536 base64 chars).</summary>
    public int MaxMetadataBlockBytes { get; init; } = 8192;

    /// <summary>
    /// Deadline for one command including its response. C# replacement for the synchronous
    /// waitForResult(); the Python client uses 2 s for ordinary replies and 30 s for s:.
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Capacity of the command queue (max.connection.commandQueueCapacity=100).</summary>
    public int CommandQueueCapacity { get; init; } = 100;

    /// <summary>Watchdog tick (LocalApp-al.xml:10-13, 5 s).</summary>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Idle activity threshold (max.local.properties:4, 30 s).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Keepalive interval (max.local.properties:5, 900 s).</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(900);

    // Gremium Runde 2: Der Live-Refresh läuft über den Idle-Watchdog, nicht über eine
    // eigene Option. Die früher nie ausgewertete `RefreshInterval` wurde entfernt;
    // `IdleTimeout` (30 s) steuert die l:-Frequenz.

    /// <summary>Health threshold used by ha-maxcube (6 h without contact).</summary>
    public TimeSpan UnreachableTimeout { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Clock used to resolve the two-digit cube year; injectable for deterministic tests.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("A cube host is required.", nameof(Host));
        }

        if (TimeProvider is null)
        {
            throw new ArgumentException("A time provider is required.", nameof(TimeProvider));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");
        }

        // CancellationTokenSource.CancelAfter rejects values above int.MaxValue milliseconds.
        var maxTimer = TimeSpan.FromMilliseconds(int.MaxValue);
        if (ConnectTimeout <= TimeSpan.Zero || HelloTimeout <= TimeSpan.Zero || SocketTimeout <= TimeSpan.Zero
            || StateReceiveTimeout <= TimeSpan.Zero || CommandTimeout <= TimeSpan.Zero
            || ConnectTimeout > maxTimer || HelloTimeout > maxTimer || SocketTimeout > maxTimer
            || StateReceiveTimeout > maxTimer || CommandTimeout > maxTimer)
        {
            throw new ArgumentException($"All timeouts must be between zero and {maxTimer.TotalDays:F1} days.");
        }

        if (MaxLineLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLineLength), MaxLineLength, "Must be positive.");
        }

        if (MaxMetadataBlockBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataBlockBytes), MaxMetadataBlockBytes, "Must be positive.");
        }

        if (CommandQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandQueueCapacity), CommandQueueCapacity, "Must be positive.");
        }

        if (WatchdogInterval <= TimeSpan.Zero || IdleTimeout <= TimeSpan.Zero
            || KeepAliveInterval <= TimeSpan.Zero || UnreachableTimeout <= TimeSpan.Zero
            || WatchdogInterval > IdleTimeout || IdleTimeout > KeepAliveInterval
            || UnreachableTimeout < KeepAliveInterval)
        {
            throw new ArgumentException("Session timeouts must be positive and ordered: watchdog <= idle <= keepalive <= unreachable.");
        }

        if (MinimumFirmwareVersion > MaximumFirmwareVersion)
        {
            throw new ArgumentException("MinimumFirmwareVersion must not exceed MaximumFirmwareVersion.");
        }
    }
}
