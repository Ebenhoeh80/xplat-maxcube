using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Framing;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class LineReaderTests
{
    [Fact]
    public async Task Reads_crlf_terminated_lines_and_then_null()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("H:abc\r\nL:def\r\n"));
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be("H:abc");
        (await reader.ReadLineAsync()).Should().Be("L:def");
        (await reader.ReadLineAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Reads_lf_terminated_lines()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("one\ntwo\n"));
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be("one");
        (await reader.ReadLineAsync()).Should().Be("two");
    }

    [Fact]
    public async Task Returns_unterminated_trailing_line_at_eof()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("partial"));
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be("partial");
        (await reader.ReadLineAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Handles_lines_split_across_reads()
    {
        await using var stream = new MemoryStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("H:12"));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("34\r\n"));
        stream.Position = 0;
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be("H:1234");
    }

    [Fact]
    public async Task Writes_crlf_terminated_line()
    {
        await using var stream = new MemoryStream();
        var reader = new LineReader(stream);

        await reader.WriteLineAsync("q:");

        Encoding.ASCII.GetString(stream.ToArray()).Should().Be("q:\r\n");
    }

    [Fact]
    public async Task Reassembles_lines_delivered_one_byte_at_a_time()
    {
        var stream = new ChunkedStream(Encoding.ASCII.GetBytes("H:1234\r\nM:5678\r\n"));
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be("H:1234");
        (await reader.ReadLineAsync()).Should().Be("M:5678");
        (await reader.ReadLineAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Reassembles_a_line_longer_than_the_read_chunk()
    {
        var payload = new string('x', 5000);
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(payload + "\r\n"));
        var reader = new LineReader(stream);

        (await reader.ReadLineAsync()).Should().Be(payload);
    }

    [Fact]
    public async Task Accepts_a_line_exactly_at_the_limit_including_terminator()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 20) + "\r\nnext\r\n"));
        var reader = new LineReader(stream, maxLineLength: 20);

        (await reader.ReadLineAsync()).Should().Be(new string('x', 20));
        (await reader.ReadLineAsync()).Should().Be("next");
    }

    [Fact]
    public async Task Accepts_exact_limit_when_cr_and_lf_arrive_in_separate_reads()
    {
        var stream = new ChunkedStream(Encoding.ASCII.GetBytes(new string('y', 20) + "\r\n"));
        var reader = new LineReader(stream, maxLineLength: 20);

        (await reader.ReadLineAsync()).Should().Be(new string('y', 20));
    }

    [Fact]
    public async Task Rejects_line_exceeding_the_configured_limit()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 21) + "\r\n"));
        var reader = new LineReader(stream, maxLineLength: 20);

        var act = () => reader.ReadLineAsync().AsTask();

        await act.Should().ThrowAsync<LineTooLongException>();
    }

    [Fact]
    public async Task Rejects_an_oversized_line_already_buffered_behind_a_short_one()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("ok\n" + new string('x', 21) + "\r\n"));
        var reader = new LineReader(stream, maxLineLength: 20);

        (await reader.ReadLineAsync()).Should().Be("ok");
        var act = () => reader.ReadLineAsync().AsTask();
        await act.Should().ThrowAsync<LineTooLongException>();
    }

    [Fact]
    public async Task Rejects_unterminated_line_exceeding_the_configured_limit()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 21)));
        var reader = new LineReader(stream, maxLineLength: 20);

        var act = () => reader.ReadLineAsync().AsTask();

        await act.Should().ThrowAsync<LineTooLongException>();
    }

    [Fact]
    public async Task Rejects_embedded_cr_or_lf_in_a_command()
    {
        await using var stream = new MemoryStream();
        var reader = new LineReader(stream);

        var act = () => reader.WriteLineAsync("s:\n00").AsTask();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*must not contain CR or LF*");
    }
}
