using System.Net;
using System.Text;
using MaxCube.Domain.Configuration;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.State;

/// <summary>
/// UDP-Datagramme des Cubes auf Port 23272 (docs/maxcube-protocol.md:476-519): Signatur
/// <c>eQ3Max*\0</c>, 10 Byte Seriennummer (mit <c>*</c> aufgefüllt), 1 Byte Opcode, Nutzlast;
/// die Antwort trägt zusätzlich die Kennung <c>&gt;</c> und denselben Opcode.
/// Firmware-Schreiben (Opcodes B/U/W) ist bewusst NICHT enthalten; R wird nur als
/// benutzerbestätigter Neustart unterstützt.
/// </summary>
public static class CubeUdpCodec
{
    public const int Port = 23272;
    public const byte GetNetworkConfiguration = (byte)'c';
    public const byte SetNetworkConfiguration = (byte)'C';
    /// <summary>Nur Neustart, keine Firmware-Aktualisierung (CubeUdpOpcode.java:10).</summary>
    public const byte Reboot = (byte)'R';

    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("eQ3Max*\0");
    private const int SerialLength = 10;
    private const int AddressBlock = 20;

    public static byte[] BuildRequest(string serialNumber, byte opcode, byte[]? payload = null)
    {
        var serial = Encoding.ASCII.GetBytes((serialNumber ?? string.Empty).PadRight(SerialLength, '*'));
        if (serial.Length != SerialLength)
        {
            throw new ArgumentException("A cube serial number has at most 10 characters.", nameof(serialNumber));
        }

        var request = new List<byte>(Signature);
        request.AddRange(serial);
        request.Add(opcode);
        if (payload is not null)
        {
            request.AddRange(payload);
        }

        return request.ToArray();
    }

    /// <summary>
    /// Prüft eine Antwort (Kennung <c>&gt;</c>, passender Opcode, passende Seriennummer) und
    /// liefert die Nutzdaten; ein unpassendes Paket wird verworfen, nicht geraten.
    /// </summary>
    public static byte[] ParseResponse(byte[] datagram, string expectedSerial, byte expectedOpcode)
    {
        ArgumentNullException.ThrowIfNull(datagram);
        if (datagram.Length < 20 || datagram[18] != (byte)'>' || datagram[19] != expectedOpcode)
        {
            throw new CubeCommunicationFailedException("Unexpected UDP response from the cube.");
        }

        var serial = Encoding.ASCII.GetString(datagram, 8, SerialLength).Trim('*', '\0', ' ');
        if (serial.Length > 0 && !string.Equals(serial, expectedSerial?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CubeCommunicationFailedException("The UDP response belongs to a different cube.");
        }

        return datagram[20..];
    }

    /// <summary>
    /// 5 × 4 Adressbytes in der Java-Reihenfolge IP, Gateway, Maske, DNS 1, DNS 2,
    /// dann DHCP-Flag und Verschlüsselungs-Flag (CubeUdpRequestFactory.java:45-47,
    /// NetworkConfiguration.java:27-38). FAKT (Hardware 25.11.2026): Die rohe
    /// c-Antwort `C0A800DE C0A80001 FFFF0000 ...` ergibt IP 192.168.0.222,
    /// Gateway 192.168.0.1 und Maske 255.255.0.0; die alte Reihenfolge war vertauscht.
    /// </summary>
    public static CubeNetworkConfiguration DecodeNetwork(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < AddressBlock + 1)
        {
            throw new CubeCommunicationFailedException(
                $"The network configuration has {data.Length} bytes; at least {AddressBlock + 1} are required.");
        }

        string Address(int index) => new IPAddress(data.AsSpan(index * 4, 4)).ToString();
        return new CubeNetworkConfiguration(
            Address(0), Address(2), Address(1), Address(3), Address(4), data[AddressBlock] != 0);
    }

    /// <summary>
    /// Schreibnutzlast; Bytes hinter dem DHCP-Flag (u. a. das Verschlüsselungs-Flag) werden aus
    /// der zuvor gelesenen Konfiguration übernommen.
    /// </summary>
    public static byte[] EncodeNetwork(CubeNetworkConfiguration configuration, byte[] current)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(current);
        var payload = new List<byte>();
        foreach (var address in new[]
        {
            configuration.IpAddress, configuration.Gateway, configuration.SubnetMask, configuration.Dns1, configuration.Dns2,
        })
        {
            if (!IPAddress.TryParse(address, out var parsed)
                || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || address.Split('.').Length != 4)
            {
                throw new ArgumentException($"'{address}' is not a valid IPv4 address.", nameof(configuration));
            }

            payload.AddRange(parsed.GetAddressBytes());
        }

        payload.Add(configuration.Dhcp ? (byte)1 : (byte)0);
        if (current.Length > AddressBlock + 1)
        {
            payload.AddRange(current.AsSpan(AddressBlock + 1).ToArray());
        }
        else
        {
            payload.Add(0);
        }

        return payload.ToArray();
    }
}
