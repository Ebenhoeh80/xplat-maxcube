using System.Globalization;
using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.Handshake;

/// <summary>
/// Parses the initial H: line according to CubeConnection.java:53-56,297-302 and
/// MaxCubeStateFactory.java:112-145. Firmware and radio address are hexadecimal;
/// field 9 is decimal, field 10 hexadecimal.
/// </summary>
public static class HelloParser
{
    public static CubeHello Parse(string? line, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith("H:", StringComparison.Ordinal))
        {
            throw new NoIncomingHelloException("No Incoming Hello");
        }

        var body = line[2..];
        var fields = body.Split(',');
        if (fields.Length < 3)
        {
            throw new MalformedHelloException("H: line needs at least serial, radio address and firmware.");
        }

        if (string.IsNullOrWhiteSpace(fields[0]))
        {
            throw new MalformedHelloException("H: line has an empty serial number.");
        }

        var radioAddress = ParseRequiredHex(fields[1], "radio address");
        var firmware = ParseRequiredHex(fields[2], "firmware");
        var (date, time) = ParseCubeDateTime(fields, timeProvider ?? TimeProvider.System);

        return new CubeHello(
            SerialNumber: fields[0],
            RadioAddress: radioAddress,
            FirmwareVersion: firmware,
            DutyCycle: TryParseHex(fields, 5),
            FreeSlots: TryParseHex(fields, 6),
            CubeDate: date,
            CubeTime: time,
            StateCubeTime: TryParseDecimal(fields, 9),
            NtpCounter: TryParseHex(fields, 10),
            RawBody: body);
    }

    private static int ParseRequiredHex(string value, string fieldName)
    {
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
        {
            throw new MalformedHelloException($"H: field '{fieldName}' is not hexadecimal: '{value}'.");
        }

        return result;
    }

    private static int? TryParseHex(string[] fields, int index)
        => index < fields.Length && int.TryParse(fields[index], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? TryParseDecimal(string[] fields, int index)
        => index < fields.Length && int.TryParse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static (DateOnly? Date, TimeOnly? Time) ParseCubeDateTime(string[] fields, TimeProvider timeProvider)
    {
        // The manufacturer sets cubeDate = null when fields 7 or 8 are missing
        // (MaxCubeStateFactory.java:131-134).
        if (fields.Length <= 8)
        {
            return (null, null);
        }

        var dateBytes = TryParseHexBytes(fields[7], 3);
        var timeBytes = TryParseHexBytes(fields[8], 2);
        if (dateBytes is null || timeBytes is null)
        {
            return (null, null);
        }

        DateOnly? date = null;
        var year = timeProvider.GetLocalNow().Year / 100 * 100 + dateBytes[0];
        if (dateBytes[1] is >= 1 and <= 12 && dateBytes[2] is >= 1 and <= 31)
        {
            try
            {
                date = new DateOnly(year, dateBytes[1], dateBytes[2]);
            }
            catch (ArgumentOutOfRangeException)
            {
                date = null;
            }
        }

        TimeOnly? time = null;
        if (timeBytes[0] < 24 && timeBytes[1] < 60)
        {
            time = new TimeOnly(timeBytes[0], timeBytes[1]);
        }

        return (date, time);
    }

    private static byte[]? TryParseHexBytes(string value, int count)
    {
        if (value.Length != count * 2)
        {
            return null;
        }

        var result = new byte[count];
        for (var i = 0; i < count; i++)
        {
            if (!byte.TryParse(value.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
            {
                return null;
            }
        }

        return result;
    }
}
