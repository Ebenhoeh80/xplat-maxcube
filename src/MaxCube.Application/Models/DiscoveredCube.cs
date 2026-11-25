namespace MaxCube.Application.Models;

/// <summary>Ein rohes UDP-Antwortpaket des Cube-Finders.</summary>
/// <param name="Payload">Datagramm-Bytes.</param>
/// <param name="SourceAddress">IPv4-Adresse des Absenders.</param>
public sealed record DiscoveryDatagram(byte[] Payload, string SourceAddress);

/// <summary>Aus der UDP-Antwort dekodierter Cube (kein Schreibzugriff).</summary>
/// <param name="SerialNumber">Zehn Byte Seriennummer aus Bytes 8..17.</param>
/// <param name="FirmwareVersion">Dezimalwert aus Bytes 24..25 (big-endian).</param>
/// <param name="Bootloader">True bei Signatur <c>eQ3MaxBl</c>.</param>
/// <param name="IpAddress">IPv4-Adresse aus dem Datagramm, nicht aus dem Payload.</param>
public sealed record DiscoveredCube(
    string SerialNumber,
    int FirmwareVersion,
    bool Bootloader,
    string IpAddress);
