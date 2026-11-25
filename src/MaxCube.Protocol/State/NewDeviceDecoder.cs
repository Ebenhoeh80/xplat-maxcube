using System.Text;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Protocol.State;

/// <summary>Ein im Anlernmodus gefundenes Gerät (N:-Antwort).</summary>
public sealed record NewDevice(
    DeviceType DeviceType,
    int NumericDeviceType,
    RadioAddress RadioAddress,
    string SerialNumber);

/// <summary>
/// Dekodiert den Base64-Körper einer N:-Zeile: 1 Byte Gerätetyp, 3 Byte Funkadresse,
/// 10 Byte Seriennummer; HeatingThermostatPlus wird zu HeatingThermostat normalisiert
/// (NewDeviceSerializer.java:29-49, docs/maxcube-protocol.md:294).
/// </summary>
public static class NewDeviceDecoder
{
    private const int PayloadLength = 14;

    /// <summary>Liefert null für einen leeren Körper (Ende des Anlernmodus) oder ein unbrauchbares Paket.</summary>
    public static NewDevice? Decode(string? responseLine)
    {
        if (string.IsNullOrEmpty(responseLine))
        {
            return null;
        }

        // ExecuteAsync liefert den Körper hinter "N:"; eine volle Zeile wird ebenfalls akzeptiert.
        var body = responseLine.StartsWith("N:", StringComparison.Ordinal) ? responseLine[2..] : responseLine;
        if (body.Length == 0)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(body.Split('\n')[0]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length < PayloadLength)
        {
            return null;
        }

        var numeric = (sbyte)bytes[0];
        var type = ((int)numeric).FromValue();
        if (type == DeviceType.Invalid)
        {
            return null;
        }

        if (type == DeviceType.HeatingThermostatPlus)
        {
            type = DeviceType.HeatingThermostat;
        }

        var address = RadioAddress.FromBytes(bytes[1], bytes[2], bytes[3]);
        var serial = Encoding.UTF8.GetString(bytes, 4, 10).TrimEnd(' ', '\0');
        return new NewDevice(type, numeric, address, serial);
    }
}
