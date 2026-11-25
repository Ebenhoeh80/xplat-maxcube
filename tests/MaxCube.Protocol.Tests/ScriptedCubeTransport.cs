namespace MaxCube.Protocol.Tests;

/// <summary>Scripted transport for tests: replayed inbound lines and recorded outbound lines.</summary>
internal sealed class ScriptedCubeTransport : ICubeTransport
{
    private readonly Queue<string?> _inbound;

    public ScriptedCubeTransport(params string?[] inbound) => _inbound = new Queue<string?>(inbound);

    public List<string> SentLines { get; } = new();
    public bool IsConnected { get; private set; }

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_inbound.Count == 0 ? null : _inbound.Dequeue());

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        SentLines.Add(line);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Transport that never answers; used to verify the H: hello timeout.</summary>
internal sealed class BlockingCubeTransport : ICubeTransport
{
    public bool IsConnected => true;

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
