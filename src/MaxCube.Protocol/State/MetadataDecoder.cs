using System.Text;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.State;

public sealed record MetadataRoom(int Id, string Name, RadioAddress MasterRadioAddress);

public sealed record MetadataDevice(
    DeviceType DeviceType,
    int NumericDeviceType,
    RadioAddress RadioAddress,
    string SerialNumber,
    string Name,
    int RoomId);

/// <summary>
/// Räume/Geräte aus dem Base64-Inhalt der M:-Blöcke. Version 1 ist der Legacy-Fall ohne
/// Magic/Version und ohne Master-Adresse/DaylightSaving.
/// </summary>
public sealed record CubeMetadataSnapshot(
    int Version,
    IReadOnlyList<MetadataRoom> Rooms,
    IReadOnlyList<MetadataDevice> Devices,
    bool DaylightSaving);

/// <summary>
/// Dekodiert das Java-Metadatenformat ohne Reflexion. Belege:
/// MetaDataSerializer.java:34-43 (Version, Feldreihenfolge), VersionedObjectSerializer.java:9,26-38
/// (Magic 86/Version), ListSerializer.java:19-35 (1-Byte-Count), UIntSerializer.java:18-52
/// (big-endian), StringSerializer.java:14-31 (1-Byte-Länge + UTF-8),
/// FixedStringSerializer.java:17-36 (10 Byte), BooleanSerializer.java:15-25 (1 Byte),
/// DeviceTypeSerializer.java:26-39 (1 vorzeichenbehaftetes Byte, Plus→HeatingThermostat).
/// EOF-Parität zu Java: stream.read() liefert -1, daher werden fehlende Count-Bytes als
/// leere Liste (ListSerializer.java:30-34) und ein fehlendes Daylight-Byte als false
/// (BooleanSerializer.java:23-24) gelesen. Nur Felder *innerhalb* eines Eintrags sind
/// streng: dort bedeutet abgeschnittene Nutzlast MalformedMetadataException.
/// </summary>
public static class MetadataDecoder
{
    public const byte MagicByte = 86;
    public const byte ActualVersion = 2;
    private const int SerialLength = 10;

    public static CubeMetadataSnapshot Decode(string base64)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new MalformedMetadataException("Metadata payload is not valid base64.", ex);
        }

        return Decode(bytes);
    }

    public static CubeMetadataSnapshot Decode(ReadOnlySpan<byte> payload)
    {
        var cursor = 0;
        byte version = 1;
        if (!payload.IsEmpty && payload[0] == MagicByte)
        {
            if (payload.Length < 2)
            {
                throw new MalformedMetadataException("Metadata has a magic byte but no version byte.");
            }

            version = payload[1];
            cursor = 2;
        }

        var rooms = new List<MetadataRoom>();
        // Java: Count -1 am Stream-Ende erzeugt eine leere Liste (ListSerializer.java:30-34).
        var roomCount = ReadByteOrDefault(payload, ref cursor, 0, "room count");
        for (var index = 0; index < roomCount; index++)
        {
            var id = ReadUInt(payload, ref cursor, 1, "room id");
            var name = ReadUtf8(payload, ref cursor, "room name");
            // Version 1 kennt das Feld nicht; MetaDataSerializer.java:40 setzt den Default 0.
            var master = version >= ActualVersion
                ? ReadUInt(payload, ref cursor, 3, "room master address")
                : 0;
            rooms.Add(new MetadataRoom(id, name, new RadioAddress(master)));
        }

        var devices = new List<MetadataDevice>();
        var deviceCount = ReadByteOrDefault(payload, ref cursor, 0, "device count");
        for (var index = 0; index < deviceCount; index++)
        {
            var numeric = (sbyte)ReadByte(payload, ref cursor, "device type");
            var type = ((int)numeric).FromValue();
            if (type == DeviceType.HeatingThermostatPlus)
            {
                type = DeviceType.HeatingThermostat;
            }

            var address = new RadioAddress(ReadUInt(payload, ref cursor, 3, "device radio address"));
            var serial = ReadFixedUtf8(payload, ref cursor, SerialLength, "device serial number");
            var name = ReadUtf8(payload, ref cursor, "device name");
            var roomId = ReadUInt(payload, ref cursor, 1, "device room id");
            devices.Add(new MetadataDevice(type, numeric, address, serial, name, roomId));
        }

        // BooleanSerializer bekommt bei Version 1 den Default true (MetaDataSerializer.java:42).
        // Bei Version 2 liefert ein fehlendes Byte -1 → false (BooleanSerializer.java:23-24).
        var daylightSaving = version >= ActualVersion
            ? ReadByteOrDefault(payload, ref cursor, 0, "daylight saving") > 0
            : true;

        // Java liest nur die bekannten Properties; Restbytes werden ignoriert
        // (ObjectSerializer.java:38-47). Das ist bewusst keine strenge Längenprüfung.
        return new CubeMetadataSnapshot(version, rooms, devices, daylightSaving);
    }

    /// <summary>Java-Parität für Count-Bytes und das Daylight-Byte: -1 am Stream-Ende.</summary>
    private static byte ReadByteOrDefault(ReadOnlySpan<byte> payload, ref int cursor, byte defaultValue, string field)
        => cursor < payload.Length ? payload[cursor++] : defaultValue;

    private static byte ReadByte(ReadOnlySpan<byte> payload, ref int cursor, string field)
    {
        if (cursor >= payload.Length)
        {
            throw new MalformedMetadataException($"Metadata ended before {field}.");
        }

        return payload[cursor++];
    }

    private static int ReadUInt(ReadOnlySpan<byte> payload, ref int cursor, int size, string field)
    {
        var value = 0;
        for (var index = 0; index < size; index++)
        {
            value = (value << 8) | ReadByte(payload, ref cursor, field);
        }

        return value;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> payload, ref int cursor, string field)
    {
        var length = ReadByte(payload, ref cursor, $"{field} length");
        return ReadFixedUtf8(payload, ref cursor, length, field);
    }

    private static string ReadFixedUtf8(ReadOnlySpan<byte> payload, ref int cursor, int length, string field)
    {
        if (cursor + length > payload.Length)
        {
            throw new MalformedMetadataException($"Metadata ended inside {field}.");
        }

        var value = Encoding.UTF8.GetString(payload.Slice(cursor, length));
        cursor += length;
        return value;
    }
}
