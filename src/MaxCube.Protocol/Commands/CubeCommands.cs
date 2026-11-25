using System.Globalization;
using System.Text;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.Commands;

/// <summary>Wire-format helpers. Hex is lowercase with fixed width (Conversion.java:11-44).</summary>
internal static class WireFormat
{
    public static string Hex(int value, int digits)
        => (value & (digits >= 8 ? -1 : (1 << (digits * 4)) - 1)).ToString("x" + digits, CultureInfo.InvariantCulture);

    public static string Hex24(int value) => Hex(value, 6);

    public static string Base64(byte[] bytes) => Convert.ToBase64String(bytes);

    public static byte[] RadioAddressBytes(int address)
        => new[] { (byte)((address >> 16) & 0xFF), (byte)((address >> 8) & 0xFF), (byte)(address & 0xFF) };
}

/// <summary>
/// Base for commands with one expected response prefix. r: is always an error; every other
/// unexpected prefix is an unknown opcode and never silently accepted
/// (roadmap.md AP-1.4 acceptance criteria 2 and 3). Response prefixes come from the
/// decompiled commands, e.g. l:-&gt;L: (ListDevicesCubeCommand.java:19-24) and
/// f:-&gt;F: (QueryNTPServerCommand.java:28-33).
/// </summary>
public abstract class ResponseCommand<TResult> : CubeCommand<TResult>
{
    private readonly string _prefix;

    protected ResponseCommand(string prefix) : this(prefix, prefix) { }

    protected ResponseCommand(string prefix, string wireLine)
    {
        _prefix = prefix;
        WireLine = wireLine;
    }

    public override string WireLine { get; }

    public override string? ResponsePrefix => _prefix;

    protected abstract TResult ParseBody(string body);

    protected sealed override TResult ParseResponse(string? line)
    {
        if (line is null)
        {
            throw new CubeCommunicationFailedException("Cube closed the connection before the command response.");
        }

        if (line.StartsWith("r:", StringComparison.Ordinal))
        {
            throw new CubeCommandRejectedException(line);
        }

        if (!line.StartsWith(_prefix, StringComparison.Ordinal))
        {
            throw new UnknownCubeOpcodeException(line);
        }

        return ParseBody(line[_prefix.Length..]);
    }
}

/// <summary>Commands on the ACK path: u:, i:, m:, t:, w:, v:, z: (CubeConnectionCommand.java:52-59).</summary>
public class AcknowledgedCommand : ResponseCommand<bool>
{
    public AcknowledgedCommand(string wireLine) : base("A:", wireLine) { }

    protected override bool ParseBody(string body) => true;
}

public sealed class ListDevicesCommand : ResponseCommand<string>
{
    public ListDevicesCommand() : base("L:", "l:") { }

    protected override string ParseBody(string body) => body;
}

public sealed class GetConfigurationCommand : ResponseCommand<string>
{
    public GetConfigurationCommand(int radioAddress)
        : base("C:", "c:" + WireFormat.Hex24(radioAddress)) { }

    protected override string ParseBody(string body) => body;
}

/// <summary>
/// One metadata block: raw UTF-8 bytes, decimal two-digit index, one A: per block
/// (SendMetadataCubeCommand.java:31-51). Splitting a document into blocks is a caller task.
/// </summary>
public sealed class SendMetadataBlockCommand : AcknowledgedCommand
{
    public const int BlockSize = 1900;

    public SendMetadataBlockCommand(int index, byte[] block)
        : base(BuildWireLine(index, block))
    {
    }

    private static string BuildWireLine(int index, byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (index is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Metadata index must be 0..99.");
        }

        if (block.Length > BlockSize)
        {
            throw new ArgumentException($"A metadata block must not exceed {BlockSize} bytes.", nameof(block));
        }

        var text = Encoding.UTF8.GetString(block);
        // Der Transport fügt CRLF selbst an. Enthielte der Block CR/LF, entstünden
        // mehrere Zeilen und damit mehrere Kommandos (SendMetadataCubeCommand.java:42-49
        // hängt CRLF ebenfalls erst nach dem Block an).
        if (text.Contains('\r') || text.Contains('\n'))
        {
            throw new ArgumentException("A metadata block must not contain CR or LF.", nameof(block));
        }

        return $"m:{index:D2}," + text;
    }
}

/// <summary>
/// n: enters inclusion mode and then waits for the first N: line. The cube sends N: only
/// when a new device appears or when the inclusion window ends (up to 60 s by default),
/// so this command gets its own timeout instead of the ordinary 30 s command timeout
/// (FetchNewDevicesCubeCommand.java:39-65; CubeConnection.java:48,53).
/// </summary>
public sealed class StartInclusionCommand : ResponseCommand<string>
{
    private readonly int _timeoutSeconds;

    public StartInclusionCommand(int timeoutSeconds)
        : base("N:", "n:" + CheckTimeout(timeoutSeconds))
    {
        _timeoutSeconds = timeoutSeconds;
    }

    public override global::MaxCube.Protocol.Commands.InclusionModeEffect InclusionModeEffect
        => global::MaxCube.Protocol.Commands.InclusionModeEffect.Enter;

    public override TimeSpan? Timeout => TimeSpan.FromSeconds(Math.Max(70, _timeoutSeconds + 10));

    public override global::MaxCube.Protocol.Commands.InclusionModeEffect ResolveInclusionModeEffect(string? response)
        // An empty N: body ends inclusion mode (FetchNewDevicesCubeCommand.java:56-62).
        => string.IsNullOrEmpty(response) || response.Length <= 2
            ? global::MaxCube.Protocol.Commands.InclusionModeEffect.Exit
            : global::MaxCube.Protocol.Commands.InclusionModeEffect.Enter;

    protected override string ParseBody(string body) => body;

    private static string CheckTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        return WireFormat.Hex(timeoutSeconds, 4);
    }
}

/// <summary>
/// x: is answered by one or more N: lines; the empty N: body ends the inclusion mode
/// (CancelInclusionModeCubeCommand.java:35-49).
/// </summary>
public sealed class CancelInclusionCommand : ResponseCommand<int>
{
    public CancelInclusionCommand() : base("N:", "x:") { }

    public override bool AllowedDuringInclusion => true;

    public override global::MaxCube.Protocol.Commands.InclusionModeEffect InclusionModeEffect
        => global::MaxCube.Protocol.Commands.InclusionModeEffect.Exit;

    public override ResponseReadMode ResponseMode => ResponseReadMode.UntilEmptyBody;

    public override int MaxResponses => 64;

    protected override int ParseBody(string body) => body.Split('\n').Length;
}

/// <summary>q: expects no response; the cube closes the stream (DisconnectCubeCommand.java:13-19).</summary>
public sealed class DisconnectCommand : CubeCommand<bool>
{
    private readonly TimeSpan? _timeout;

    public DisconnectCommand(TimeSpan? timeout = null) => _timeout = timeout;

    public override string WireLine => "q:";

    public override bool ExpectsResponse => false;

    public override bool AllowedDuringInclusion => true;

    public override TimeSpan? Timeout => _timeout;

    protected override bool ParseResponse(string? line) => true;
}

public sealed class SetUrlCommand : AcknowledgedCommand
{
    public SetUrlCommand(string url) : base("u:" + (url ?? throw new ArgumentNullException(nameof(url)))) { }
}

public sealed class SetIntervalCommand : AcknowledgedCommand
{
    public SetIntervalCommand(int intervalMilliseconds)
        : base("i:" + WireFormat.Hex(intervalMilliseconds, 8) + ",00000000") { }
}

public sealed record RadioStatus(int DutyCycle, bool CommandDiscarded, int FreeMemorySlots);

/// <summary>s: / r: commands answer S:; discarded=='1' is a communication failure
/// (DeviceSendCommand.java:34-49).</summary>
public class SendRadiogramCommand : ResponseCommand<RadioStatus>
{
    public SendRadiogramCommand(byte[] payload)
        : base("S:", "s:" + WireFormat.Base64(payload ?? throw new ArgumentNullException(nameof(payload)))) { }

    protected override RadioStatus ParseBody(string body) => RadioStatusParser.Parse(body);
}

public sealed class ResetDeviceCommand : ResponseCommand<RadioStatus>
{
    public ResetDeviceCommand(int radioAddress)
        : base("S:", "r:01," + WireFormat.Base64(WireFormat.RadioAddressBytes(radioAddress))) { }

    protected override RadioStatus ParseBody(string body) => RadioStatusParser.Parse(body);
}

internal static class RadioStatusParser
{
    public static RadioStatus Parse(string body)
    {
        var args = body.Split(',');
        if (args.Length != 3
            || !int.TryParse(args[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var duty)
            || !int.TryParse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var free))
        {
            throw new CubeCommunicationFailedException($"Invalid S: status '{body}' (expected three arguments).");
        }

        var discarded = args[1].Length > 0 && args[1][0] == '1';
        if (discarded)
        {
            throw new CubeCommunicationFailedException("Command discarded");
        }

        return new RadioStatus(duty, discarded, free);
    }
}

public sealed class EncryptCommand : ResponseCommand<string>
{
    public EncryptCommand(string plaintext)
        : base("E:", "e:" + (plaintext ?? throw new ArgumentNullException(nameof(plaintext)))) { }

    protected override string ParseBody(string body) => body;
}

public sealed class DecryptCommand : ResponseCommand<string>
{
    public DecryptCommand(string base64Cipher)
        : base("D:", "d:" + (base64Cipher ?? throw new ArgumentNullException(nameof(base64Cipher)))) { }

    protected override string ParseBody(string body) => body;
}

/// <summary>a: resets the gateway; the manufacturer client reads no ACK (ResetGatewayCubeCommand.java:11-12).</summary>
public sealed class ResetGatewayCommand : CubeCommand<bool>
{
    public override string WireLine => "a:";

    public override bool ExpectsResponse => false;

    protected override bool ParseResponse(string? line) => true;
}

public sealed class DeleteDevicesCommand : AcknowledgedCommand
{
    public DeleteDevicesCommand(IReadOnlyList<int> radioAddresses, bool force = false)
        : base(BuildWireLine(radioAddresses, force))
    {
    }

    private static string BuildWireLine(IReadOnlyList<int> radioAddresses, bool force)
    {
        ArgumentNullException.ThrowIfNull(radioAddresses);
        if (radioAddresses.Count is < 1 or > 255)
        {
            throw new ArgumentException("DeleteDevices needs 1..255 addresses.", nameof(radioAddresses));
        }

        var bytes = radioAddresses.SelectMany(WireFormat.RadioAddressBytes).ToArray();
        return $"t:{WireFormat.Hex(bytes.Length / 3, 2)},{(force ? '1' : '0')},{WireFormat.Base64(bytes)}";
    }
}

public sealed class SetPushButtonConfigurationCommand : AcknowledgedCommand
{
    public SetPushButtonConfigurationCommand(byte[] configuration)
        : base("w:" + WireFormat.Base64(RequireLength(configuration, 66))) { }

    private static byte[] RequireLength(byte[] bytes, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != length)
        {
            throw new ArgumentException($"Expected {length} bytes, got {bytes.Length}.", nameof(bytes));
        }

        return bytes;
    }
}

public sealed class SetTimezoneConfigurationCommand : AcknowledgedCommand
{
    public SetTimezoneConfigurationCommand(byte[] configuration, int ntpUtcSeconds = 0)
        : base(BuildWireLine(configuration, ntpUtcSeconds))
    {
    }

    private static string BuildWireLine(byte[] configuration, int ntpUtcSeconds)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Length != 24)
        {
            throw new ArgumentException($"Expected 24 timezone bytes, got {configuration.Length}.", nameof(configuration));
        }

        var line = "v:" + WireFormat.Base64(configuration);
        return ntpUtcSeconds > 0 ? line + "," + ntpUtcSeconds.ToString("x", CultureInfo.InvariantCulture) : line;
    }
}

public sealed class SetNtpServerCommand : ResponseCommand<string>
{
    public SetNtpServerCommand(params string[] servers)
        : base("F:", BuildWireLine(servers))
    {
    }

    protected override string ParseBody(string body) => body;

    private static string BuildWireLine(string[] servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        if (servers.Length is < 1 or > 2 || servers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("One or two non-empty NTP servers are required.", nameof(servers));
        }

        return "f:" + string.Join(',', servers);
    }
}

public sealed class QueryNtpServerCommand : ResponseCommand<string>
{
    public QueryNtpServerCommand() : base("F:", "f:") { }

    protected override string ParseBody(string body) => body;
}

public enum WakeupContext
{
    All,
    Device,
    Group,
}

public sealed class WakeupCommand : AcknowledgedCommand
{
    public WakeupCommand(WakeupContext context, int destination, int durationMinutes = 30)
        : base(BuildWireLine(context, destination, durationMinutes))
    {
    }

    private static string BuildWireLine(WakeupContext context, int destination, int durationMinutes)
    {
        if (durationMinutes is < 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        }

        var code = context switch
        {
            WakeupContext.All => "A",
            WakeupContext.Device => "D",
            WakeupContext.Group => "G",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };

        var line = "z:" + WireFormat.Hex(durationMinutes, 2) + "," + code;
        return context switch
        {
            WakeupContext.Device => line + "," + WireFormat.Hex24(destination),
            WakeupContext.Group => line + "," + WireFormat.Hex(destination, 2),
            _ => line,
        };
    }
}
