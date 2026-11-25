namespace MaxCube.Protocol.Exceptions;

/// <summary>Base type for all errors raised by the MAX! Cube protocol layer.</summary>
public class CubeProtocolException : Exception
{
    protected CubeProtocolException(string message) : base(message) { }
    protected CubeProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Mirrors the original CubeCommunicationFailedException (CubeConnection.java:64,66):
/// both "No Incoming Hello" and "incompatible firmware version" are communication failures.
/// </summary>
public class CubeCommunicationFailedException : CubeProtocolException
{
    public CubeCommunicationFailedException(string message) : base(message) { }
    public CubeCommunicationFailedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The first line was missing or did not start with the H: prefix.</summary>
/// <remarks>CubeConnection.java:53-54,66.</remarks>
public sealed class NoIncomingHelloException : CubeCommunicationFailedException
{
    public NoIncomingHelloException(string message) : base(message) { }
}

/// <summary>The H: line was syntactically invalid (missing or unparsable mandatory fields).</summary>
public sealed class MalformedHelloException : CubeCommunicationFailedException
{
    public MalformedHelloException(string message) : base(message) { }
    public MalformedHelloException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The announced firmware is outside the supported window (273..275 by default).</summary>
public sealed class IncompatibleFirmwareException : CubeCommunicationFailedException
{
    public IncompatibleFirmwareException(int firmwareVersion, int minimum, int maximum)
        : base($"Firmware {firmwareVersion} is outside the supported range {minimum}..{maximum}.")
    {
        FirmwareVersion = firmwareVersion;
        Minimum = minimum;
        Maximum = maximum;
    }

    public int FirmwareVersion { get; }
    public int Minimum { get; }
    public int Maximum { get; }
}

/// <summary>The M: payload does not match the Java metadata serializer layout.</summary>
public sealed class MalformedMetadataException : CubeCommunicationFailedException
{
    public MalformedMetadataException(string message) : base(message) { }
    public MalformedMetadataException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Transport-level failure (connect, read, write, timeout).</summary>
public sealed class CubeTransportException : CubeProtocolException
{
    public CubeTransportException(string message) : base(message) { }
    public CubeTransportException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A peer sent more bytes than the configured line limit without a line terminator.</summary>
public sealed class LineTooLongException : CubeProtocolException
{
    public LineTooLongException(int limit)
        : base($"Received more than {limit} bytes without a line terminator.") { }
}
