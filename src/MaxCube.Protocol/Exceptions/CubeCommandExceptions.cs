namespace MaxCube.Protocol.Exceptions;

/// <summary>
/// A command was rejected because the cube is in inclusion (pairing) mode.
/// Mirrors CubeIsInInclusionModeException (CubeConnection.java:264-275).
/// </summary>
public sealed class CubeIsInInclusionModeException : CubeProtocolException
{
    public CubeIsInInclusionModeException()
        : base("The cube is in inclusion mode; only fetch/cancel inclusion and disconnect are allowed.") { }
}

/// <summary>A command did not receive the expected response prefix.</summary>
public sealed class UnexpectedCubeResponseException : CubeCommunicationFailedException
{
    public UnexpectedCubeResponseException(string message) : base(message) { }
}

/// <summary>The cube answered with an opcode the client cannot handle; never treat as success.</summary>
public sealed class UnknownCubeOpcodeException : CubeCommunicationFailedException
{
    public UnknownCubeOpcodeException(string line)
        : base($"Unknown cube response opcode: '{line}'.") { }
}

/// <summary>The cube answered r: instead of the expected acknowledgement.</summary>
public sealed class CubeCommandRejectedException : CubeCommunicationFailedException
{
    public CubeCommandRejectedException(string line)
        : base($"Cube rejected the command with '{line}'.") { }
}
