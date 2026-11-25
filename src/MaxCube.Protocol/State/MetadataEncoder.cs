using System.Text;
using MaxCube.Domain.Aggregates;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.State;

/// <summary>
/// Kodiert Räume und Geräte in das Metadatenformat Version 2 (Gegenstück zu
/// <see cref="MetadataDecoder"/>: Magic 86, Version 2, 1-Byte-Count je Liste,
/// UInt big-endian, 1-Byte-Länge + UTF-8, 10-Byte-Seriennummer, Daylight-Byte;
/// MetaDataSerializer.java:34-43). Strukturänderungen (Umbenennen, Löschen, Verschieben,
/// Reihenfolge) schreiben diese Daten blockweise mit <c>m:</c> zurück.
/// </summary>
/// <remarks>
/// Weil ein falsch kodierter Metadatenblock die Raumstruktur des Cubes zerstören würde,
/// gibt es eine Sicherheitsprüfung (<see cref="VerifyRoundTrip"/>): Der unveränderte,
/// vom Cube empfangene Zustand muss sich byte-identisch neu kodieren lassen, bevor
/// irgendetwas gesendet wird. Das ist hardwarefrei testbar und fängt Formatabweichungen ab.
/// </remarks>
public static class MetadataEncoder
{
    private const int MaxCount = 255;

    /// <summary>Baut den Snapshot aus dem aktuellen Aggregat (Reihenfolge = Raum-/Geräteliste).</summary>
    public static CubeMetadataSnapshot FromCube(Cube cube)
    {
        ArgumentNullException.ThrowIfNull(cube);
        return new CubeMetadataSnapshot(
            MetadataDecoder.ActualVersion,
            cube.Rooms.Select(room => new MetadataRoom(room.Id, room.Name, room.MasterRadioAddress)).ToArray(),
            cube.Devices.Select(device => new MetadataDevice(
                device.DeviceType,
                device.NumericDeviceType,
                device.RadioAddress,
                device.SerialNumber.Value,
                device.Name,
                device.RoomId)).ToArray(),
            cube.DaylightSaving);
    }

    public static byte[] Encode(CubeMetadataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Rooms.Count > MaxCount || snapshot.Devices.Count > MaxCount)
        {
            throw new MalformedMetadataException("Metadata supports at most 255 rooms and 255 devices.");
        }

        var bytes = new List<byte> { MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion };
        bytes.Add((byte)snapshot.Rooms.Count);
        foreach (var room in snapshot.Rooms)
        {
            if (room.Id is < 0 or > 255)
            {
                throw new MalformedMetadataException($"Room id {room.Id} does not fit in one byte.");
            }

            bytes.Add((byte)room.Id);
            WriteUtf8(bytes, room.Name);
            bytes.AddRange(room.MasterRadioAddress.ToBytes());
        }

        bytes.Add((byte)snapshot.Devices.Count);
        foreach (var device in snapshot.Devices)
        {
            if (device.RoomId is < 0 or > 255)
            {
                throw new MalformedMetadataException($"Room id {device.RoomId} does not fit in one byte.");
            }

            bytes.Add(unchecked((byte)(sbyte)device.NumericDeviceType));
            bytes.AddRange(device.RadioAddress.ToBytes());
            bytes.AddRange(SerialBytes(device.SerialNumber));
            WriteUtf8(bytes, device.Name);
            bytes.Add((byte)device.RoomId);
        }

        bytes.Add(snapshot.DaylightSaving ? (byte)1 : (byte)0);
        return bytes.ToArray();
    }

    public static string EncodeBase64(CubeMetadataSnapshot snapshot) => Convert.ToBase64String(Encode(snapshot));

    /// <summary>
    /// Zerlegt den Base64-Text in Blöcke der Protokollgröße. Der Cube setzt die Blöcke
    /// anhand des Index wieder zusammen (SendMetadataCubeCommand.java:31-51).
    /// </summary>
    public static IReadOnlyList<byte[]> SplitIntoBlocks(string base64)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64);
        var text = Encoding.ASCII.GetBytes(base64);
        var count = (text.Length + SendMetadataBlockCommand.BlockSize - 1) / SendMetadataBlockCommand.BlockSize;
        if (count > 16)
        {
            throw new MalformedMetadataException("Metadata would need more than 16 blocks (Metadata.java:10).");
        }

        var blocks = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = index * SendMetadataBlockCommand.BlockSize;
            var length = Math.Min(SendMetadataBlockCommand.BlockSize, text.Length - offset);
            blocks.Add(text.AsSpan(offset, length).ToArray());
        }

        return blocks;
    }

    /// <summary>
    /// Sicherheitsprüfung vor jedem Schreiben. Entspricht der empfangene Zustand dem
    /// Format Version 2, muss <c>Encode(Decode(original))</c> byte-identisch sein; bei
    /// Version 1 (Legacy ohne Master/Daylight) wird nur die strukturelle Rundreise
    /// geprüft, weil der Cube dann ohnehin im neuen Format neu geschrieben wird.
    /// </summary>
    /// <exception cref="MalformedMetadataException">Die Kodierung weicht vom Cube-Format ab.</exception>
    public static void VerifyRoundTrip(string originalBase64)
    {
        ArgumentException.ThrowIfNullOrEmpty(originalBase64);
        byte[] original;
        try
        {
            original = Convert.FromBase64String(originalBase64);
        }
        catch (FormatException exception)
        {
            throw new MalformedMetadataException("Received metadata is not valid base64.", exception);
        }

        var decoded = MetadataDecoder.Decode(original);
        var encoded = Encode(decoded with { Version = MetadataDecoder.ActualVersion });
        if (decoded.Version >= MetadataDecoder.ActualVersion && !encoded.AsSpan().SequenceEqual(TrimTrailing(original, encoded.Length)))
        {
            throw new MalformedMetadataException(
                "Safety check failed: re-encoding the unchanged metadata does not reproduce the cube's bytes. Nothing was sent.");
        }

        var again = MetadataDecoder.Decode(encoded);
        if (again.Rooms.Count != decoded.Rooms.Count || again.Devices.Count != decoded.Devices.Count)
        {
            throw new MalformedMetadataException("Safety check failed: structural round trip changed the metadata.");
        }
    }

    /// <summary>
    /// Java liest nur die bekannten Properties und ignoriert Restbytes
    /// (ObjectSerializer.java:38-47); für den Vergleich zählt das genutzte Präfix.
    /// </summary>
    private static ReadOnlySpan<byte> TrimTrailing(byte[] original, int length)
        => original.Length >= length ? original.AsSpan(0, length) : original;

    private static byte[] SerialBytes(string serial)
    {
        var bytes = new byte[10];
        Array.Fill(bytes, (byte)' ');
        var source = Encoding.UTF8.GetBytes(serial ?? string.Empty);
        if (source.Length > bytes.Length)
        {
            throw new MalformedMetadataException("Device serial number exceeds 10 bytes.");
        }

        Array.Copy(source, bytes, source.Length);
        return bytes;
    }

    private static void WriteUtf8(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (encoded.Length > 255)
        {
            throw new MalformedMetadataException("A metadata string exceeds 255 bytes.");
        }

        bytes.Add((byte)encoded.Length);
        bytes.AddRange(encoded);
    }
}
