using System.Globalization;
using MaxCube.Domain.Exceptions;

namespace MaxCube.Domain.ValueObjects;

/// <summary>
/// 24-Bit-Funkadresse, im Radiogramm drei Bytes big-endian (Device.java:126-133).
/// BEOBACHTUNG: die H:-Beispiele tragen sechs Hex-Ziffern (CubeConnection.java:55-56),
/// parseHeader prüft aber nur parseInt(...,16) ohne Längenregel (`:297-302`).
/// INFERENZ: MAX1034/MAX1035 deuten wir aus den Fehlercode-Namen (errorcodes.properties:36-37)
/// als Länge bzw. Hex-Format. MAX1022 (`:24`) bedeutet im Java-Code „Gerät existiert nicht“;
/// die Verwendung als 24-Bit-Bereichsgrenze ist eine bewusste Umdeutung für dieses Wertobjekt.
/// </summary>
public readonly record struct RadioAddress
{
    public const int MaxValue = 0xFFFFFF;
    public const int HexLength = 6;

    public int Value { get; }

    public RadioAddress(int value)
    {
        if ((uint)value > MaxValue)
        {
            throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress,
                $"Radio address 0x{value:X} is outside 0..0xFFFFFF.");
        }

        Value = value;
    }

    public static RadioAddress Parse(string hex)
    {
        if (!TryParse(hex, out var address))
        {
            if (hex is null || hex.Length != HexLength)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.RadioAddressHasInvalidLength,
                    $"Radio address must have {HexLength} hex digits.");
            }

            throw new DomainValidationException(
                DomainValidationCodes.RadioAddressIsNotHex,
                $"Radio address '{hex}' is not hexadecimal.");
        }

        return address;
    }

    public static bool TryParse(string? hex, out RadioAddress address)
    {
        address = default;
        if (hex is null || hex.Length != HexLength)
        {
            return false;
        }

        if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        address = new RadioAddress(value);
        return true;
    }

    public static RadioAddress FromBytes(byte high, byte mid, byte low)
        => new((high << 16) | (mid << 8) | low);

    public byte[] ToBytes()
        => [(byte)((Value >> 16) & 0xFF), (byte)((Value >> 8) & 0xFF), (byte)(Value & 0xFF)];

    public override string ToString() => Value.ToString("X6", CultureInfo.InvariantCulture);
}
