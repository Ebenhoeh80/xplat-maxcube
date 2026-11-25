namespace MaxCube.Protocol.Tests;

/// <summary>Stream that fails on read, to verify that transports wrap I/O errors.</summary>
internal sealed class ThrowingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException("Simulated cube read failure.");

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException("Simulated cube write failure.");

    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated failure.");
    public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Simulated failure.");
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
