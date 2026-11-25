namespace MaxCube.Protocol.Commands;

/// <summary>
/// One command for the single writer worker. The wire line is always sent with CRLF by the
/// transport. Response handling is explicit: "no response" commands (q:, a:) set
/// <see cref="ExpectsResponse"/> to false.
/// </summary>
public interface ICubeCommand
{
    string WireLine { get; }

    bool ExpectsResponse { get; }

    /// <summary>Only fetch/cancel inclusion and disconnect may run while pairing is active.</summary>
    bool AllowedDuringInclusion { get; }

    /// <summary>State transitions performed by n:/x: (CubeConnection.java:45-49,59-62).</summary>
    InclusionModeEffect InclusionModeEffect { get; }

    /// <summary>Cancel-inclusion reads N: lines until an empty body arrives.</summary>
    ResponseReadMode ResponseMode { get; }

    int MaxResponses { get; }

    /// <summary>Response prefix used to filter lines in multi-response modes; null means no filter.</summary>
    string? ResponsePrefix { get; }

    /// <summary>Override for commands whose response legitimately takes longer than CommandTimeout.</summary>
    TimeSpan? Timeout { get; }

    /// <summary>Effect to apply after the response arrived (an empty N: exits inclusion mode).</summary>
    InclusionModeEffect ResolveInclusionModeEffect(string? response);
}

public enum InclusionModeEffect
{
    None,
    Enter,
    Exit,
}

public enum ResponseReadMode
{
    Single,
    UntilEmptyBody,
}

/// <summary>Typed base class; TResult is the parsed response (e.g. the ACK string or a duty status).</summary>
internal interface IPendingCommand
{
    void Complete(string? line);
    void Fail(Exception exception);
    void Cancel(CancellationToken token);
}

public abstract class CubeCommand<TResult> : ICubeCommand, IPendingCommand
{
    private readonly TaskCompletionSource<TResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public abstract string WireLine { get; }

    public virtual bool ExpectsResponse => true;

    public virtual bool AllowedDuringInclusion => false;

    public virtual InclusionModeEffect InclusionModeEffect => InclusionModeEffect.None;

    public virtual ResponseReadMode ResponseMode => ResponseReadMode.Single;

    public virtual int MaxResponses => 1;

    public virtual string? ResponsePrefix => null;

    public virtual TimeSpan? Timeout => null;

    public virtual InclusionModeEffect ResolveInclusionModeEffect(string? response) => InclusionModeEffect;

    public Task<TResult> Result => _completion.Task;

    protected abstract TResult ParseResponse(string? line);

    void IPendingCommand.Complete(string? line) => _completion.TrySetResult(ParseResponse(line));

    void IPendingCommand.Fail(Exception exception) => _completion.TrySetException(exception);

    void IPendingCommand.Cancel(CancellationToken token) => _completion.TrySetCanceled(token);
}
