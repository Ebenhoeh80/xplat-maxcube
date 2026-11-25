namespace MaxCube.Protocol.Tests;

/// <summary>Stream that returns at most one byte per read, to exercise line reassembly.</summary>
internal sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private int _position;

    public ChunkedStream(byte[] data) => _data = data;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_position >= _data.Length || buffer.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        buffer.Span[0] = _data[_position++];
        return ValueTask.FromResult(1);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _data.Length || count == 0)
        {
            return 0;
        }

        buffer[offset] = _data[_position++];
        return 1;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
