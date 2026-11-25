namespace MaxCube.Protocol.Handshake;

/// <summary>
/// Parsed H: line. Mandatory: serial, radio address, firmware (fields 0..2).
/// Optional fields are null when absent or unparsable; fields 3 and 4 are
/// deliberately ignored because the manufacturer client ignores them too
/// (MaxCubeStateFactory.java:112-145).
/// </summary>
public sealed record CubeHello(
    string SerialNumber,
    int RadioAddress,
    int FirmwareVersion,
    int? DutyCycle,
    int? FreeSlots,
    DateOnly? CubeDate,
    TimeOnly? CubeTime,
    int? StateCubeTime,
    int? NtpCounter,
    string RawBody)
{
    /// <summary>
    /// Never log the serial number or raw body: diagnostics must redact identifiers
    /// (ha-maxcube-erkenntnisse.md §7.5).
    /// </summary>
    public override string ToString()
        => $"CubeHello(Serial=***, RadioAddress=0x{RadioAddress:X6}, Firmware={FirmwareVersion})";
}
