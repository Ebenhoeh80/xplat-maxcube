using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.State;

/// <summary>
/// Dekodiert das Wochenprofil aus dem gerätespezifischen Teil der C:-Konfiguration.
/// Belege: HeatingThermostatDeviceState.java:256 und WallMountedThermostatDeviceState.java:209
/// (bytePosition = 11), WeekTemperatureProfileDeviceConfigurationParameterDeserializer.java:10,16-55
/// (7 Tage Samstag zuerst, 13 Einträge à 2 Byte, Zeit = (value &amp; 511) * 5 min,
/// Temperatur = (value &gt;&gt; 9) &amp; 63), WeekTemperatureProfileBuilder.java:28-53
/// (der erste 00:00-Eintrag beendet den Tag, fehlende Einträge werden mit 21,5 °C ergänzt),
/// Temperature.java:76-80 (Drahtwert / 2).
/// </summary>
public static class WeekProfileDecoder
{
    public const int ProfileOffset = 11;
    private const int EntriesPerDay = 13;
    private const int EntrySize = 2;
    private const int ProfileLength = EntriesPerDay * EntrySize * 7;

    // Java liest Samstag zuerst (...Deserializer.java:21-27); C# nutzt Monday=1..Sunday=7.
    private static readonly DayOfWeek[] DayOrder =
    [
        DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
        DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    ];

    /// <summary>
    /// Liefert null, wenn der C:-Block kein vollständiges Profil enthält. Ein unplausibler
    /// Einzelfag wird verworfen, ohne den Connect zu faulten (Parse-Fehler faulten nicht).
    /// </summary>
    public static WeekTemperatureProfile? Decode(ReadOnlySpan<byte> config)
    {
        if (config.Length < ProfileOffset + ProfileLength)
        {
            return null;
        }

        var profile = new WeekTemperatureProfile();
        var cursor = ProfileOffset;
        foreach (var day in DayOrder)
        {
            var points = new List<SwitchPoint>(EntriesPerDay);
            var ended = false;
            for (var index = 0; index < EntriesPerDay; index++)
            {
                // Auch nach dem ersten 00:00 müssen alle 13 Einträge konsumiert werden,
                // sonst verschiebt sich der Cursor für die folgenden Tage.
                var value = (config[cursor] << 8) | config[cursor + 1];
                cursor += EntrySize;
                if (ended)
                {
                    continue;
                }

                var minutes = (value & 511) * 5;
                var hour = (minutes / 60) % 24;
                var minute = minutes % 60;
                points.Add(new SwitchPoint(
                    new TimeOnly(hour, minute),
                    Temperature.FromWire((value >> 9) & 63)));

                if (hour == 0 && minute == 0)
                {
                    ended = true;
                }
            }

            if (!ended)
            {
                points.Add(new SwitchPoint(TimeOnly.MinValue, new Temperature(21.5)));
            }

            try
            {
                profile.SetDay(new DayProfile(day, points));
            }
            catch (DomainValidationException)
            {
                // Ein einzelner unvernünftiger Tag darf nicht den Gesamtzustand zerstören.
            }
        }

        return profile;
    }
}
