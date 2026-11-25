using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using FluentAssertions;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class CubeCommandQueueTests
{
    [Fact]
    public async Task Hundred_parallel_commands_are_processed_serially_in_enqueue_order()
    {
        var replies = Enumerable.Range(0, 100).Select(i => $"A:{i:D3}").ToArray();
        var transport = new ScriptedCubeTransport(replies);
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => queue.ExecuteAsync(new AckCommand($"u:{i:D3}", $"A:{i:D3}")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().Equal(replies);
        transport.SentLines.Should().Equal(Enumerable.Range(0, 100).Select(i => $"u:{i:D3}"));
        queue.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Commands_never_overlap_on_the_transport()
    {
        var transport = new OverlapDetectingTransport();
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => queue.ExecuteAsync(new AckCommand($"u:{i}", "A:")))
            .ToArray();
        await Task.WhenAll(tasks);

        transport.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task Inclusion_mode_rejects_normal_commands_until_cancel()
    {
        var transport = new ScriptedCubeTransport("N:AAAA", "N:", "A:done");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        (await queue.ExecuteAsync(new AckCommand(
            "n:003c", "N:AAAA", allowedDuringInclusion: true, inclusionModeEffect: InclusionModeEffect.Enter)))
            .Should().Be("N:AAAA");
        queue.IsInclusionMode.Should().BeTrue();

        var rejected = () => queue.ExecuteAsync(new AckCommand("s:xx", "A:"));
        await rejected.Should().ThrowAsync<CubeIsInInclusionModeException>();

        (await queue.ExecuteAsync(new AckCommand(
            "x:", "N:", allowedDuringInclusion: true, inclusionModeEffect: InclusionModeEffect.Exit)))
            .Should().Be("N:");
        queue.IsInclusionMode.Should().BeFalse();
        (await queue.ExecuteAsync(new AckCommand("q:probe", "A:done"))).Should().Be("A:done");
    }

    [Fact]
    public async Task Transport_failure_fails_the_current_and_all_waiting_commands()
    {
        var transport = new GatedCubeTransport();
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var first = queue.ExecuteAsync(new AckCommand("i:first", "A:"));
        await transport.WriteStarted;
        var waiting = Enumerable.Range(0, 10)
            .Select(i => queue.ExecuteAsync(new AckCommand($"i:{i}", "A:")))
            .ToArray();
        using var enqueueTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (queue.QueuedCount < 10)
        {
            await Task.Delay(5, enqueueTimeout.Token);
        }

        transport.Release();

        await first.Invoking(task => task).Should().ThrowAsync<IOException>();
        foreach (var task in waiting)
        {
            await task.Invoking(t => t).Should().ThrowAsync<CubeCommunicationFailedException>()
                .WithMessage("*command ignored*");
        }

        queue.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task Command_timeout_faults_the_queue_with_a_timeout_exception()
    {
        var options = new CubeConnectionOptions { CommandTimeout = TimeSpan.FromMilliseconds(100) };
        await using var transport = new BlockingCubeTransport();
        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();

        var act = () => queue.ExecuteAsync(new AckCommand("l:", "L:"));

        var failure = await act.Should().ThrowAsync<CubeCommunicationFailedException>();
        failure.Which.InnerException.Should().BeOfType<TimeoutException>();
        queue.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task Queue_is_faulted_before_the_timeout_reaches_the_caller()
    {
        // Gremium Runde 2 (N3): Fault() muss vor Fail() liegen. Andernfalls kann ein
        // Aufrufer IsFaulted=false sehen, obwohl die Queue bereits alle Späteren ablehnt.
        var options = new CubeConnectionOptions { CommandTimeout = TimeSpan.FromMilliseconds(100) };
        await using var transport = new BlockingCubeTransport();
        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();

        var faultedAtCaller = false;
        try
        {
            await queue.ExecuteAsync(new AckCommand("l:", "L:"));
        }
        catch (CubeCommunicationFailedException)
        {
            faultedAtCaller = queue.IsFaulted;
        }

        faultedAtCaller.Should().BeTrue();
    }

    [Fact]
    public async Task A_command_specific_timeout_overrides_the_global_deadline()
    {
        var options = new CubeConnectionOptions { CommandTimeout = TimeSpan.FromSeconds(30) };
        await using var transport = new BlockingCubeTransport();
        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var act = () => queue.ExecuteAsync(new AckCommand(
            "l:", "L:", timeout: TimeSpan.FromMilliseconds(100)));

        await act.Should().ThrowAsync<CubeCommunicationFailedException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        queue.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task Closed_connection_before_response_is_a_communication_failure()
    {
        var transport = new ScriptedCubeTransport();
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var act = () => queue.ExecuteAsync(new AckCommand("l:", "L:"));

        await act.Should().ThrowAsync<CubeCommunicationFailedException>().WithMessage("*closed the connection*");
    }

    [Fact]
    public async Task Commands_are_rejected_before_start()
    {
        await using var queue = new CubeCommandQueue(new ScriptedCubeTransport(), new CubeConnectionOptions());

        var act = () => queue.ExecuteAsync(new AckCommand("q:", "A:"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Start()*");
    }

    [Fact]
    public async Task Fault_rejects_a_writer_waiting_on_a_full_queue()
    {
        var transport = new GatedCubeTransport();
        var options = new CubeConnectionOptions { CommandQueueCapacity = 1 };
        await using var queue = new CubeCommandQueue(transport, options);
        queue.Start();

        var first = queue.ExecuteAsync(new AckCommand("i:first", "A:"));
        await transport.WriteStarted;
        var queued = queue.ExecuteAsync(new AckCommand("i:second", "A:"));
        using var enqueueTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (queue.QueuedCount < 1)
        {
            await Task.Delay(5, enqueueTimeout.Token);
        }

        var blockedWriter = queue.ExecuteAsync(new AckCommand("i:third", "A:"));

        transport.Release();

        await first.Invoking(task => task).Should().ThrowAsync<IOException>();
        await queued.Invoking(task => task).Should().ThrowAsync<CubeCommunicationFailedException>()
            .WithMessage("*command ignored*");
        await blockedWriter.Invoking(task => task).Should().ThrowAsync<CubeTransportException>()
            .WithMessage("*closed*");
        await queue.Invoking(q => q.ExecuteAsync(new AckCommand("i:after", "A:"))).Should()
            .ThrowAsync<CubeTransportException>();
    }

    [Fact]
    public async Task Parse_failure_rejects_only_its_command_without_faulting()
    {
        var transport = new ScriptedCubeTransport("Z:unknown", "A:");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        await queue.Invoking(q => q.ExecuteAsync(new AckCommand("u:bad", "A:"))).Should()
            .ThrowAsync<UnexpectedCubeResponseException>();
        queue.IsFaulted.Should().BeFalse();
        (await queue.ExecuteAsync(new AckCommand("u:good", "A:"))).Should().Be("A:");
    }

    [Fact]
    public async Task Inclusion_guard_also_applies_at_execution_time()
    {
        var transport = new GatedSuccessTransport();
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var blocker = queue.ExecuteAsync(new AckCommand("i:blocker", "A:"));
        await transport.WriteStarted;
        var enter = queue.ExecuteAsync(new AckCommand(
            "n:003c", "A:", allowedDuringInclusion: true, inclusionModeEffect: InclusionModeEffect.Enter));
        var tooLate = queue.ExecuteAsync(new AckCommand("s:late", "A:"));

        transport.Release();
        await blocker;
        await enter;

        await tooLate.Invoking(task => task).Should().ThrowAsync<CubeIsInInclusionModeException>();
        queue.IsFaulted.Should().BeFalse();
        queue.IsInclusionMode.Should().BeTrue();
        (await queue.ExecuteAsync(new AckCommand(
            "x:", "A:", allowedDuringInclusion: true, inclusionModeEffect: InclusionModeEffect.Exit)))
            .Should().Be("A:");
        transport.SentLines.Should().NotContain("s:late");
    }

    [Fact]
    public async Task Missing_empty_response_line_faults_the_stream()
    {
        var transport = new ScriptedCubeTransport("N:AAAA", "N:BBBB");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var command = new AckCommand(
            "x:", "N:", responseMode: ResponseReadMode.UntilEmptyBody, maxResponses: 2);
        await queue.Invoking(q => q.ExecuteAsync(command)).Should()
            .ThrowAsync<CubeCommunicationFailedException>().WithMessage("*desynchronized*");
        queue.IsFaulted.Should().BeTrue();
    }

    private sealed class AckCommand : CubeCommand<string>
    {
        private readonly string _wireLine;
        private readonly string _expectedPrefix;

        public AckCommand(
            string wireLine,
            string expectedPrefix,
            bool allowedDuringInclusion = false,
            InclusionModeEffect inclusionModeEffect = InclusionModeEffect.None,
            ResponseReadMode responseMode = ResponseReadMode.Single,
            int maxResponses = 1,
            TimeSpan? timeout = null)
        {
            _wireLine = wireLine;
            _expectedPrefix = expectedPrefix;
            AllowedDuringInclusion = allowedDuringInclusion;
            InclusionModeEffect = inclusionModeEffect;
            ResponseMode = responseMode;
            MaxResponses = maxResponses;
            Timeout = timeout;
        }

        public override string WireLine => _wireLine;

        public override bool AllowedDuringInclusion { get; }

        public override InclusionModeEffect InclusionModeEffect { get; }

        public override ResponseReadMode ResponseMode { get; }

        public override int MaxResponses { get; }

        public override TimeSpan? Timeout { get; }

        protected override string ParseResponse(string? line)
            => line is not null && line.StartsWith(_expectedPrefix, StringComparison.Ordinal)
                ? line
                : throw new UnexpectedCubeResponseException($"Expected {_expectedPrefix}, got '{line}'.");
    }

    /// <summary>Blocks the first write until the test has queued all waiting commands.</summary>
    private sealed class GatedCubeTransport : ICubeTransport
    {
        private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writes;

        public Task WriteStarted => _writeStarted.Task;

        public bool IsConnected => true;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("A:");

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writes) == 1)
            {
                _writeStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                throw new IOException("Simulated socket failure.");
            }
        }

        public void Release() => _release.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Detects two commands using the transport at the same time.</summary>
    private sealed class OverlapDetectingTransport : ICubeTransport
    {
        private int _active;
        private int _max;

        public int MaxConcurrent => Volatile.Read(ref _max);

        public bool IsConnected => true;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            await Task.Yield();
            Exit();
            return "A:";
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            Enter();
            await Task.Yield();
            Exit();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            while (active > (observed = Volatile.Read(ref _max))
                && Interlocked.CompareExchange(ref _max, active, observed) != observed)
            {
                // retry until the maximum is updated
            }
        }

        private void Exit() => Interlocked.Decrement(ref _active);
    }

    /// <summary>Blocks only the first write, then succeeds; records all written lines.</summary>
    private sealed class GatedSuccessTransport : ICubeTransport
    {
        private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _sentLines = new();
        private int _writes;

        public Task WriteStarted => _writeStarted.Task;

        public IReadOnlyList<string> SentLines { get; }

        public bool IsConnected => true;

        public GatedSuccessTransport() => SentLines = _sentLines;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("A:");

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            lock (_sentLines)
            {
                _sentLines.Add(line);
            }

            if (Interlocked.Increment(ref _writes) == 1)
            {
                _writeStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Release() => _release.TrySetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
