using System.Text;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.Framing;

/// <summary>
/// Reads CR/LF- or LF-terminated lines from a stream and writes CRLF-terminated lines.
/// The MAX! Cube uses a line-based text protocol; the Python reference reads until
/// "\r\n" (commander.py), the Java original uses BufferedReader.readLine().
/// Both terminators are accepted. Like BufferedReader, the reader buffers up to one
/// chunk of look-ahead; buffered bytes belong to the next line and are never lost.
/// </summary>
public sealed class LineReader
{
    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private readonly byte[] _chunk = new byte[4096];
    private readonly List<byte> _line = new(256);
    private readonly int _maxLineLength;
    private int _scanOffset;

    public LineReader(Stream stream, Encoding? encoding = null, int maxLineLength = 65536)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (maxLineLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLineLength));
        }

        _maxLineLength = maxLineLength;
    }

    /// <summary>
    /// Returns the next line without its terminator, or null at EOF with no buffered bytes.
    /// A trailing unterminated line is returned as-is, matching BufferedReader.readLine().
    /// </summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            // Also check bytes buffered by a previous read: a complete short line may
            // already sit in front of an oversized one.
            EnforceLineLimit();

            for (var i = _scanOffset; i < _line.Count; i++)
            {
                if (_line[i] != (byte)'\n')
                {
                    continue;
                }

                var length = i > 0 && _line[i - 1] == (byte)'\r' ? i - 1 : i;
                var text = _encoding.GetString(_line.GetRange(0, length).ToArray());
                _line.RemoveRange(0, i + 1);
                _scanOffset = 0;
                return text;
            }

            _scanOffset = _line.Count;

            var read = await _stream.ReadAsync(_chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_line.Count == 0)
                {
                    return null;
                }

                var text = _encoding.GetString(_line.ToArray());
                _line.Clear();
                _scanOffset = 0;
                return text;
            }

            for (var i = 0; i < read; i++)
            {
                _line.Add(_chunk[i]);
            }

            EnforceLineLimit();
        }
    }

    /// <summary>
    /// Checks the pending line only, not the read-ahead chunk: _line may already contain
    /// the next line(s). The terminator is not counted.
    /// </summary>
    private void EnforceLineLimit()
    {
        var newline = _line.IndexOf((byte)'\n');
        var length = newline >= 0 ? newline : _line.Count;
        if (newline > 0 && _line[newline - 1] == (byte)'\r')
        {
            length--;
        }
        else if (newline < 0 && _line.Count > 0 && _line[^1] == (byte)'\r')
        {
            // A CR at a chunk boundary is probably the first half of CRLF.
            length--;
        }

        if (length <= _maxLineLength)
        {
            return;
        }

        _line.Clear();
        _scanOffset = 0;
        throw new LineTooLongException(_maxLineLength);
    }

    /// <summary>Writes one CRLF-terminated command line and flushes the stream.</summary>
    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Contains('\r') || line.Contains('\n'))
        {
            throw new ArgumentException("A command line must not contain CR or LF characters.", nameof(line));
        }

        var bytes = _encoding.GetBytes(line + "\r\n");
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
