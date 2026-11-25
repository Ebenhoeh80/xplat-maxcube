using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Protocol.Radiograms;

/// <summary>
/// Serialisiert ein Tagesprofil in ein oder zwei 0x10-Telegramme. Byte-Layout und
/// Auffüllregel folgen SetRoomWeekTemperatureProfile.java:89-100: fehlende Punkte werden
/// mit dem letzten Punkt wiederholt; der zweite Telegramm-Flag liegt in Byte 10 Bit 4.
/// </summary>
public static class WeekProfileSerializer
{
    public const int MaxPointsFirstTelegram = 7;
    public const int MaxPointsSecondTelegram = 6;
    public const int MaxPoints = MaxPointsFirstTelegram + MaxPointsSecondTelegram;

    public static IReadOnlyList<byte[]> Serialize(DayProfile profile, RadioAddress master, int roomId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Points.Count == 0 || profile.Points.Count > MaxPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile), "A day needs 1..13 switch points (SetRoomWeekTemperatureProfile.java:81-84).");
        }

        var result = new List<byte[]> { SerializePart(profile, master, roomId, telegram: 0) };
        if (profile.Points.Count > MaxPointsFirstTelegram)
        {
            result.Add(SerializePart(profile, master, roomId, telegram: 1));
        }

        return result;
    }

    private static byte[] SerializePart(DayProfile profile, RadioAddress master, int roomId, int telegram)
    {
        var builder = RadiogramBuilder.ForRoom(FrameTypes.ConfigWeekTemperatureProfile, master, roomId)
            .WriteWeekDay(10, profile.Day)
            .WriteBool(10, 4, telegram == 1);

        var max = telegram == 0 ? MaxPointsFirstTelegram : MaxPointsSecondTelegram;
        var offset = MaxPointsFirstTelegram * telegram;
        var last = profile.Points[^1];
        for (var index = 0; index < max; index++)
        {
            var point = offset + index < profile.Points.Count ? profile.Points[offset + index] : last;
            builder.WriteSwitchPoint(index, point.Until, point.Temperature);
        }

        // Java-Ergebnislänge: 25 Byte für 7 Punkte, 23 Byte für 6 Punkte.
        return builder.ToBytes(minimumLength: 0);
    }
}
