using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Radiograms;

/// <summary>Frame-Typen aus FrameType.java:13,21 (nur die von der App benutzten).</summary>
public static class FrameTypes
{
    public const byte ConfigWeekTemperatureProfile = 0x10; // FrameType.java:13
    public const byte ConfigTemperatures = 0x11;           // FrameType.java:14
    public const byte ConfigValveFunctions = 0x12;         // FrameType.java:15
    public const byte AddLinkPartner = 0x20;               // FrameType.java:16
    public const byte RemoveLinkPartner = 0x21;            // FrameType.java:17
    public const byte SetGroupAddress = 0x22;              // FrameType.java:18
    public const byte RemoveGroupAddress = 0x23;           // FrameType.java:19
    public const byte ControlMode = 0x40;                  // FrameType.java:21
    public const byte SwitchDisplaySetPointActual = 0x82;  // FrameType.java:31, am Wandthermostat bestätigt
}

/// <summary>
/// Bitfeld-Schreiber mit der LSB-first-Semantik von RadiogramSpecification.createPayload()
/// (RadiogramSpecification.java:114-138, ByteUtil.java:8-20). Das Resultat ist immer ein
/// 17-Byte-Radiogramm, wie es auch schedule.py:112-118 für 0x10 erzeugt. Java kürzt bei
/// kürzeren Telegrammen auf die höchste benutzte Bitposition (RadiogramSpecification.java:140-147);
/// die feste Länge ist eine bewusste Kompatibilitätsentscheidung (INFERENZ), keine Beobachtung.
/// </summary>
public sealed class RadiogramBuilder
{
    /// <summary>Kürzeste Länge für Steuer-/Konfigurationstelegramme (Python-Kompatibilität).</summary>
    public const int PayloadLength = 17;

    // Wochenprofile sind länger: 7 Schaltpunkte → 25 Byte, 6 → 23 Byte
    // (SetRoomWeekTemperatureProfile.java:89-100, RadiogramSpecification.java:140-147).
    private const int BufferLength = 32;

    private readonly byte[] _bytes = new byte[BufferLength];
    private int _highestUsedByte = -1;

    private RadiogramBuilder() { }

    /// <summary>
    /// Adressiert ein einzelnes Gerät (ClientCommand.java:39-41). Die 24-Bit-Adresse wird
    /// big-endian in die Bytes 6..8 geschrieben: `ByteUtil.fromInt(value,24)` liefert
    /// `b[0]=MSB` (ByteUtil.java:8-20,23-33), und schedule.py hängt die Hex-Adresse
    /// unverändert an. Ein einzelnes WriteBits(6,0,24,...) wäre dagegen little-endian.
    /// </summary>
    public static RadiogramBuilder ForDevice(byte frame, RadioAddress device)
    {
        var builder = new RadiogramBuilder();
        builder.WriteBits(2, 0, 8, frame);
        var address = device.ToBytes();
        builder.WriteBits(6, 0, 8, address[0]);
        builder.WriteBits(7, 0, 8, address[1]);
        builder.WriteBits(8, 0, 8, address[2]);
        return builder;
    }

    /// <summary>Gruppenkommando: Byte 1 Bit 2 gesetzt, Byte 9 = RaumId (AbstractRoomClientCommand.java:45-50).</summary>
    public static RadiogramBuilder ForRoom(byte frame, RadioAddress master, int roomId)
    {
        var builder = ForDevice(frame, master);
        builder.WriteBool(1, 2, true);
        builder.WriteBool(1, 1, false);
        builder.WriteBits(9, 0, 8, roomId);
        return builder;
    }

    /// <summary>Schreibt die niedrigsten <paramref name="length"/> Bits LSB-first an die Position.</summary>
    public RadiogramBuilder WriteBits(int bytePosition, int bitPosition, int length, int value)
    {
        var mask = length >= 32 ? uint.MaxValue : (1u << length) - 1u;
        var bits = (uint)value & mask;
        for (var index = 0; index < length; index++)
        {
            var absolute = (bytePosition * 8) + bitPosition + index;
            if (absolute >= _bytes.Length * 8)
            {
                throw new ArgumentOutOfRangeException(nameof(bytePosition), "Radiogram payload exceeds 32 bytes.");
            }

            _highestUsedByte = Math.Max(_highestUsedByte, absolute / 8);
            var targetBit = absolute % 8;
            if ((bits & (1u << index)) != 0)
            {
                _bytes[absolute / 8] |= (byte)(1 << targetBit);
            }
            else
            {
                _bytes[absolute / 8] &= (byte)~(1 << targetBit);
            }
        }

        return this;
    }

    public RadiogramBuilder WriteBool(int bytePosition, int bitPosition, bool value)
        => WriteBits(bytePosition, bitPosition, 1, value ? 1 : 0);

    public RadiogramBuilder WriteByte(int bytePosition, byte value)
        => WriteBits(bytePosition, 0, 8, value);

    /// <summary>OFF→9, ON→61, sonst Wert*2; RadiogramSpecification.java:96-111.</summary>
    public int ToWireTemperature(Temperature temperature)
        => temperature.IsOff ? 9 : temperature.IsOn ? 61 : (int)(temperature.Value * 2);

    public RadiogramBuilder WriteTemperature(int bytePosition, int bitPosition, Temperature temperature)
        => WriteBits(bytePosition, bitPosition, 6, ToWireTemperature(temperature));

    /// <summary>Auto=0, Permanently=1, Temporary=2; RadiogramSpecification.java:72-95.</summary>
    public RadiogramBuilder WriteControlMode(int bytePosition, int bitPosition, RoomControlMode mode)
    {
        var value = mode switch
        {
            RoomControlMode.Auto => 0,
            RoomControlMode.Permanently => 1,
            RoomControlMode.Temporary => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "NotAvailable cannot be sent."),
        };
        return WriteBits(bytePosition, bitPosition, 2, value);
    }

    /// <summary>(day+1)%7 in 3 Bits; RadiogramSpecification.java:281-285.</summary>
    public RadiogramBuilder WriteWeekDay(int bytePosition, DayOfWeek day)
        => WriteBits(bytePosition, 0, 3, ((int)day + 1) % 7);

    /// <summary>Offset in 4 Bits: (value+3,5)*2; RadiogramSpecification.java:222-225.</summary>
    public RadiogramBuilder WriteOffsetTemperature(int bytePosition, double offset)
        => WriteBits(bytePosition, 0, 4, (int)((offset + 3.5) * 2));

    /// <summary>Minuten/5 in 4 Bits; RadiogramSpecification.java:228-231.</summary>
    public RadiogramBuilder WriteWindowOpenTime(int bytePosition, int minutes)
        => WriteBits(bytePosition, 0, 4, minutes / 5);

    /// <summary>Monat (1..12) in 4 Bits; RadiogramSpecification.java:239-243.</summary>
    public RadiogramBuilder WriteMonth(int bytePosition, int bitPosition, DateTimeOffset date)
        => WriteBits(bytePosition, bitPosition, 4, date.Month);

    /// <summary>Tag in 5 Bits; RadiogramSpecification.java:233-237.</summary>
    public RadiogramBuilder WriteDay(int bytePosition, int bitPosition, DateTimeOffset date)
        => WriteBits(bytePosition, bitPosition, 5, date.Day);

    /// <summary>Jahr-2000 in 7 Bits; RadiogramSpecification.java:245-249.</summary>
    public RadiogramBuilder WriteYear(int bytePosition, int bitPosition, DateTimeOffset date)
        => WriteBits(bytePosition, bitPosition, 7, date.Year - 2000);

    /// <summary>
    /// 30-Minuten-Raster; RadiogramSpecification.java:193-205. Bei min&gt;=45 wird die
    /// Stunde erhöht und minCount bleibt 0 (Java: `if (minute &gt;= 45) hour++;`).
    /// </summary>
    public RadiogramBuilder WriteTime30Minutes(int bytePosition, int bitPosition, DateTimeOffset date)
    {
        var hour = date.Hour;
        var minuteCount = 0;
        if (date.Minute >= 45)
        {
            hour++;
        }
        else if (date.Minute > 15 && date.Minute < 45)
        {
            minuteCount = 1;
        }

        return WriteBits(bytePosition, bitPosition, 6, (hour * 2) + minuteCount);
    }

    /// <summary>
    /// 9-Bit-Zeit (5-Minuten-Raster) mit Überlaufbit im vorherigen Byte;
    /// RadiogramSpecification.java:207-220, byte-abgleich-python-java.md:22,31-33.
    /// </summary>
    public RadiogramBuilder WriteSwitchPoint(int index, TimeOnly until, Temperature temperature)
    {
        var time = (until.Hour * 12) + (until.Minute / 5);
        if (time == 0)
        {
            time = 288;
        }

        var timeByte = 12 + (2 * index);
        var temperatureByte = 11 + (2 * index);
        if (time > 255)
        {
            WriteBool(temperatureByte, 0, true);
            time -= 256;
        }

        WriteBits(timeByte, 0, 8, time);
        WriteBits(temperatureByte, 1, 6, ToWireTemperature(temperature));
        return this;
    }

    /// <summary>
    /// Standardmäßig 17 Byte; mit <paramref name="minimumLength"/> = 0 die von Java
    /// berechnete minimale Länge (RadiogramSpecification.java:114-125,140-147).
    /// </summary>
    public byte[] ToBytes(int minimumLength = PayloadLength)
    {
        var length = Math.Clamp(Math.Max(_highestUsedByte + 1, minimumLength), 0, BufferLength);
        return _bytes.AsSpan(0, length).ToArray();
    }
}
