using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Domain.Schedules;

/// <summary>Ein Schaltpunkt: bis <see cref="Until"/> gilt <see cref="Temperature"/>.</summary>
public sealed record SwitchPoint(TimeOnly Until, Temperature Temperature);

/// <summary>
/// Tagesprofil mit 1..13 Schaltpunkten. 7 + 6 ergeben die zwei Telegramme aus
/// SetRoomWeekTemperatureProfile.java:81-100 (siehe auch roadmap.md:13 Schaltpunkte).
/// </summary>
public sealed record DayProfile(DayOfWeek Day, IReadOnlyList<SwitchPoint> Points)
{
    /// <summary>
    /// Prüft die Voraussetzungen für ein sendbares Profil. 00:00 ist im Drahtformat 24:00
    /// (RadiogramBuilder.WriteSwitchPoint) und deshalb nur als letzter Punkt erlaubt.
    /// </summary>
    public static void Validate(DayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var points = profile.Points;
        if (points.Count is < 1 or > WeekTemperatureProfile.MaxPointsPerDay)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"A day needs 1..{WeekTemperatureProfile.MaxPointsPerDay} switch points.");
        }

        TimeOnly? previous = null;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.Until.Minute % 5 != 0 || point.Until.Second != 0 || point.Until.Millisecond != 0)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation, $"Switch point {index + 1} must be on a 5-minute grid.");
            }

            if (point.Temperature.Value < 4.5 || point.Temperature.Value > 30.5
                || Math.Abs((point.Temperature.Value * 2) - Math.Round(point.Temperature.Value * 2)) > 1e-9)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation,
                    $"Switch point {index + 1} must be between 4.5 and 30.5 °C in 0.5 °C steps.");
            }

            var isEndOfDay = point.Until == TimeOnly.MinValue;
            if (isEndOfDay && index != points.Count - 1)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation, "00:00 (24:00) is only allowed as the last switch point.");
            }

            var comparable = isEndOfDay ? TimeOnly.MaxValue : point.Until;
            if (previous is not null && comparable <= previous.Value)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation, "Switch point times must be strictly ascending.");
            }

            previous = comparable;
        }

        if (points[^1].Until != TimeOnly.MinValue)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "The last switch point must be 24:00 (enter 00:00).");
        }
    }
}

/// <summary>
/// Wochenprofil eines Raums. Der Cube erlaubt maximal 13 Punkte pro Tag; die Grenze wird
/// hier bewusst durchgesetzt (INFERENZ: die Java-AL-Klassen prüfen sie nicht selbst).
/// </summary>
public sealed class WeekTemperatureProfile
{
    public const int MaxPointsPerDay = 13;

    private readonly Dictionary<DayOfWeek, DayProfile> _days = new();

    public IReadOnlyCollection<DayProfile> Days => _days.Values.OrderBy(d => (int)d.Day).ToArray();

    public DayProfile? GetDay(DayOfWeek day) => _days.TryGetValue(day, out var profile) ? profile : null;

    public void SetDay(DayProfile profile)
    {
        DayProfile.Validate(profile);
        _days[profile.Day] = profile;
    }

    public void ClearDay(DayOfWeek day) => _days.Remove(day);
}
