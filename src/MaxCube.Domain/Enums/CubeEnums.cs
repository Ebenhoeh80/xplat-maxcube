using MaxCube.Domain.Exceptions;

namespace MaxCube.Domain.Enums;

/// <summary>Numerische Codes und fromValue aus DeviceType.java:4-30.</summary>
public enum DeviceType
{
    Invalid = 256,
    HeatingThermostat = 1,
    HeatingThermostatPlus = 2,
    WallMountedThermostat = 3,
    ShutterContact = 4,
    PushButton = 5,
}

public static class DeviceTypeExtensions
{
    /// <summary>Unbekannte Codes ergeben Invalid (DeviceType.java:22-30).</summary>
    public static DeviceType FromValue(this int value) => value switch
    {
        1 => DeviceType.HeatingThermostat,
        2 => DeviceType.HeatingThermostatPlus,
        3 => DeviceType.WallMountedThermostat,
        4 => DeviceType.ShutterContact,
        5 => DeviceType.PushButton,
        _ => DeviceType.Invalid,
    };
}

/// <summary>RoomControlMode.java:4-9.</summary>
public enum RoomControlMode
{
    Auto,
    Permanently,
    Temporary,
    NotAvailable,
}

/// <summary>RoomTemperatureMode.java:4-9.</summary>
public enum RoomTemperatureMode
{
    Eco,
    Comfort,
    Normal,
    NotAvailable,
}

/// <summary>DeviceStateInfo.java:4-8.</summary>
public enum DeviceStateInfo
{
    Valid,
    NotInitialized,
    OutOfDate,
}

/// <summary>RadioState.java:4-11.</summary>
public enum RadioState
{
    Ok,
    Error,
    ControlFailed,
    ConfigFailed,
    AssignFailed,
    DeleteFailed,
}

/// <summary>
/// Achtung: nicht System.DayOfWeek! DayOfWeek.java:6-45 nutzt Monday=1..Sunday=7;
/// fromValue wirft bei unbekannten Werten (`:37-45`), fromCalendar rechnet mit
/// Calendar.DAY_OF_WEEK (Sunday=1) nach `((tag + 5) % 7) + 1` (`:33-35`).
/// </summary>
public enum DayOfWeek
{
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Sunday = 7,
}

public static class DayOfWeekExtensions
{
    public static DayOfWeek FromMaxValue(this int value) => value switch
    {
        1 => DayOfWeek.Monday,
        2 => DayOfWeek.Tuesday,
        3 => DayOfWeek.Wednesday,
        4 => DayOfWeek.Thursday,
        5 => DayOfWeek.Friday,
        6 => DayOfWeek.Saturday,
        7 => DayOfWeek.Sunday,
        _ => throw new DomainValidationException(
            DomainValidationCodes.Validation, $"Got value: {value}"),
    };

    public static DayOfWeek FromCalendarDay(int calendarDay)
        => FromMaxValue(((calendarDay + 5) % 7) + 1);
}

/// <summary>PushButtonKeyMode.java:4-10.</summary>
public enum PushButtonKeyMode
{
    Auto,
    AutoWithTemperature,
    Eco,
    Comfort,
    Manual,
}
