using System.Text;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.State;

/// <summary>
/// Liest und schreibt die Bereiche der Cube-eigenen C:-Konfiguration, die der Original-
/// Client verwaltet (docs/maxcube-protocol.md:269-272, :310-322):
/// Push-Button-Templates ab Offset 19 und 52 (je 33 Byte, Tastenbyte an Position 11),
/// Zeitzonen ab Offset 214 (Winter) und 226 (Sommer, je 12 Byte).
/// </summary>
/// <remarks>
/// Das Schreiben ändert ausschließlich die dokumentierten Felder und reicht alle übrigen
/// Bytes unverändert zurück; ein zu kurzer oder fremder Block führt zu einem Fehler statt
/// zu einem geratenen Schreibvorgang. INFERENZ: die Feldpositionen folgen der Doku und sind
/// nicht gegen echte Hardware gemessen.
/// </remarks>
public static class CubeConfigurationCodec
{
    public const int PushButtonOffsetUpper = 19;
    public const int PushButtonOffsetLower = 52;
    public const int PushButtonTemplateLength = 33;
    public const int PushButtonKeyByte = 11;
    public const int TimeZoneOffsetWinter = 214;
    public const int TimeZoneRuleLength = 12;
    public const int TimeZoneOffsetSummer = TimeZoneOffsetWinter + TimeZoneRuleLength;
    public const int MinimumLength = TimeZoneOffsetSummer + TimeZoneRuleLength;

    /// <summary>Dekodiert den Base64-Teil einer <c>C:&lt;adr&gt;,&lt;base64&gt;</c>-Antwort.</summary>
    public static byte[] DecodeResponse(string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(body);
        var separator = body.IndexOf(',');
        var payload = separator >= 0 ? body[(separator + 1)..] : body;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException exception)
        {
            throw new CubeCommunicationFailedException("The cube configuration is not valid base64.", exception);
        }

        if (bytes.Length < MinimumLength)
        {
            throw new CubeCommunicationFailedException(
                $"The cube configuration has {bytes.Length} bytes; at least {MinimumLength} are required.");
        }

        return bytes;
    }

    public static PushButtonConfiguration ReadPushButton(byte[] configuration)
    {
        Require(configuration);
        return new PushButtonConfiguration(
            DecodeKey(configuration[PushButtonOffsetUpper + PushButtonKeyByte]),
            DecodeKey(configuration[PushButtonOffsetLower + PushButtonKeyByte]));
    }

    /// <summary>
    /// Baut die 66 <c>w:</c>-Bytes aus der Cube-Konfiguration und ersetzt nur die beiden
    /// Tastenbytes (PushButtonConfigurationCubeCommand.java:17-49, docs:310-315).
    /// </summary>
    public static byte[] BuildPushButtonPayload(byte[] configuration, PushButtonConfiguration desired)
    {
        Require(configuration);
        ArgumentNullException.ThrowIfNull(desired);
        var payload = new byte[PushButtonTemplateLength * 2];
        Array.Copy(configuration, PushButtonOffsetUpper, payload, 0, PushButtonTemplateLength);
        Array.Copy(configuration, PushButtonOffsetLower, payload, PushButtonTemplateLength, PushButtonTemplateLength);
        payload[PushButtonKeyByte] = EncodeKey(desired.Upper);
        payload[PushButtonTemplateLength + PushButtonKeyByte] = EncodeKey(desired.Lower);
        return payload;
    }

    public static byte EncodeKey(PushButtonKeyConfiguration key)
    {
        ArgumentNullException.ThrowIfNull(key);
        int mode;
        int temperature;
        switch (key.Mode)
        {
            case PushButtonKeyMode.Auto:
                mode = 0;
                temperature = 0;
                break;
            case PushButtonKeyMode.AutoWithTemperature:
                mode = 0;
                temperature = WireTemperature(key);
                break;
            case PushButtonKeyMode.Manual:
                mode = 1;
                temperature = WireTemperature(key);
                break;
            case PushButtonKeyMode.Eco:
                mode = 1;
                temperature = 1;
                break;
            case PushButtonKeyMode.Comfort:
                mode = 1;
                temperature = 2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }

        return (byte)((mode << 6) | (temperature & 63));
    }

    public static PushButtonKeyConfiguration DecodeKey(byte value)
    {
        var mode = value >> 6;
        var temperature = value & 63;
        if (mode == 0)
        {
            return temperature == 0
                ? new PushButtonKeyConfiguration(PushButtonKeyMode.Auto, null)
                : new PushButtonKeyConfiguration(PushButtonKeyMode.AutoWithTemperature, Temperature.FromWire(temperature));
        }

        return temperature switch
        {
            1 => new PushButtonKeyConfiguration(PushButtonKeyMode.Eco, null),
            2 => new PushButtonKeyConfiguration(PushButtonKeyMode.Comfort, null),
            _ => new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, Temperature.FromWire(temperature)),
        };
    }

    public static TimeZoneConfiguration ReadTimeZone(byte[] configuration)
    {
        Require(configuration);
        return new TimeZoneConfiguration(
            DecodeRule(configuration, TimeZoneOffsetWinter),
            DecodeRule(configuration, TimeZoneOffsetSummer));
    }

    /// <summary>24 <c>v:</c>-Bytes: zuerst Winter, dann Sommer (AdjustTimezoneConfigurationCommand.java:34-39).</summary>
    public static byte[] BuildTimeZonePayload(TimeZoneConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var payload = new byte[TimeZoneRuleLength * 2];
        EncodeRule(configuration.Winter, payload, 0);
        EncodeRule(configuration.Summer, payload, TimeZoneRuleLength);
        return payload;
    }

    private static int WireTemperature(PushButtonKeyConfiguration key)
    {
        if (key.Temperature is not { } temperature)
        {
            throw new ArgumentException("This key mode requires a temperature.", nameof(key));
        }

        return (int)(temperature.Value * 2) & 63;
    }

    private static TimeZoneRule DecodeRule(byte[] bytes, int offset)
    {
        // 5 Byte Name (NUL/'0'-aufgefüllt), Monat, Wochentag % 7 (Sonntag = 0), Stunde, Offset 4 Byte big-endian.
        var name = Encoding.ASCII.GetString(bytes, offset, 5).TrimEnd('\0');
        var offsetSeconds = (bytes[offset + 8] << 24) | (bytes[offset + 9] << 16)
            | (bytes[offset + 10] << 8) | bytes[offset + 11];
        return new TimeZoneRule(name, bytes[offset + 5], bytes[offset + 6], bytes[offset + 7], offsetSeconds);
    }

    private static void EncodeRule(TimeZoneRule rule, byte[] target, int offset)
    {
        if (rule.Name.Length > 5 || rule.Name.Any(character => character > 127))
        {
            throw new ArgumentException("A time zone name has at most 5 ASCII characters.", nameof(rule));
        }

        if (rule.Month is < 1 or > 12 || rule.DayOfWeekFromSunday is < 0 or > 6 || rule.Hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Month 1..12, weekday 0..6 and hour 0..23 are required.");
        }

        var name = Encoding.ASCII.GetBytes(rule.Name);
        Array.Copy(name, 0, target, offset, name.Length);
        target[offset + 5] = (byte)rule.Month;
        target[offset + 6] = (byte)rule.DayOfWeekFromSunday;
        target[offset + 7] = (byte)rule.Hour;
        target[offset + 8] = (byte)(rule.OffsetSeconds >> 24);
        target[offset + 9] = (byte)(rule.OffsetSeconds >> 16);
        target[offset + 10] = (byte)(rule.OffsetSeconds >> 8);
        target[offset + 11] = (byte)rule.OffsetSeconds;
    }

    private static void Require(byte[] configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Length < MinimumLength)
        {
            throw new CubeCommunicationFailedException(
                $"The cube configuration has {configuration.Length} bytes; at least {MinimumLength} are required.");
        }
    }
}
