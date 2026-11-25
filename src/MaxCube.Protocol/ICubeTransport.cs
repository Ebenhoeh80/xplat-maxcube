namespace MaxCube.Protocol;

/// <summary>Stream abstraction for the cube connection. Implementations are hardware-free testable.</summary>
public interface ICubeTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns the next line without terminator, or null at end of stream.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>
    /// Variante mit eigenem Timeout, z. B. für n: mit max(70 s, inclusionSeconds + 10 s).
    /// Bestehende Transporte ignorieren den Wert über die Default-Implementierung; der
    /// TCP-Transport überschreibt beide Methoden und nutzt ihn statt des festen SocketTimeout.
    /// </summary>
    ValueTask<string?> ReadLineAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
        => ReadLineAsync(cancellationToken);

    ValueTask WriteLineAsync(string line, TimeSpan? timeout, CancellationToken cancellationToken = default)
        => WriteLineAsync(line, cancellationToken);
}
