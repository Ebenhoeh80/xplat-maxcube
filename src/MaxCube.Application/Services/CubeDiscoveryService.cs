using System.Text;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;

namespace MaxCube.Application.Services;

/// <summary>
/// Kodierung und Dekodierung des UDP-Cube-Finders. Belege:
/// <c>docs/maxcube-protocol.md:47-67</c> (Java <c>CubeFinder.java:22-28,47-51,84-106</c>).
/// Discovery liefert die Firmware dezimal, der TCP-Header dagegen hexadezimal – die
/// Werte werden deshalb nicht in den Firmware-Guard eingespeist.
/// </summary>
public static class CubeDiscoveryProtocol
{
    public const int UdpPort = 23272;
    public const int MulticastTtl = 5;
    public const int ResendCount = 3;
    public static readonly TimeSpan ResendInterval = TimeSpan.FromMilliseconds(50);
    public static readonly string MulticastGroup = "224.0.0.1";
    public static readonly string BroadcastAddress = "255.255.255.255";

    private const string RequestLiteral = "eQ3Max*\0**********I";
    private const int SerialOffset = 8;
    private const int SerialLength = 10;
    private const int FirmwareOffset = 24;

    /// <summary>19 Byte: Signatur + zehn <c>*</c> + Opcode <c>I</c>.</summary>
    public static byte[] BuildRequest() => Encoding.ASCII.GetBytes(RequestLiteral);

    /// <summary>
    /// Interpretiert eine Antwort. Ungültige oder zu kurze Pakete werden verworfen,
    /// statt einen erfundenen Cube in die Auswahl zu stellen.
    /// </summary>
    public static DiscoveredCube? TryParse(DiscoveryDatagram datagram)
    {
        ArgumentNullException.ThrowIfNull(datagram);
        var payload = datagram.Payload;
        if (payload is null || payload.Length < FirmwareOffset + 2 ||
            string.IsNullOrWhiteSpace(datagram.SourceAddress))
        {
            return null;
        }

        var signature = Encoding.ASCII.GetString(payload, 0, SerialOffset);
        var bootloader = signature switch
        {
            "eQ3MaxAp" => false,
            "eQ3MaxBl" => true,
            _ => (bool?)null,
        };
        if (bootloader is null)
        {
            return null;
        }

        var serial = Encoding.ASCII.GetString(payload, SerialOffset, SerialLength)
            .Trim('\0', ' ', '*');
        if (serial.Length == 0)
        {
            return null;
        }

        var firmware = (payload[FirmwareOffset] << 8) | payload[FirmwareOffset + 1];
        return new DiscoveredCube(serial, firmware, bootloader.Value, datagram.SourceAddress);
    }
}

/// <summary>
/// Orchestriert den Discovery-Transport und entdoppelt mehrfach empfangene Antworten.
/// Ein Bootloader-Cube verliert gegen eine reguläre Antwort derselben Seriennummer.
/// </summary>
public sealed class CubeDiscoveryService : ICubeDiscovery
{
    private readonly IDiscoveryTransport _transport;

    public CubeDiscoveryService(IDiscoveryTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<IReadOnlyList<DiscoveredCube>> DiscoverAsync(
        TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var request = CubeDiscoveryProtocol.BuildRequest();
        var datagrams = await _transport.ProbeAsync(request, duration, cancellationToken)
            .ConfigureAwait(false);

        var unique = new Dictionary<string, DiscoveredCube>(StringComparer.Ordinal);
        foreach (var datagram in datagrams)
        {
            var cube = CubeDiscoveryProtocol.TryParse(datagram);
            if (cube is null)
            {
                continue;
            }

            if (!unique.TryGetValue(cube.SerialNumber, out var known) ||
                (known.Bootloader && !cube.Bootloader))
            {
                unique[cube.SerialNumber] = cube;
            }
        }

        return unique.Values.ToArray();
    }
}
