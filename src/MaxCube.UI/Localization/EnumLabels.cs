using MaxCube.Domain.Enums;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.Localization;

/// <summary>
/// Übersetzt Domain-Enums und Preset-Schlüssel in Katalogschlüssel. Die Enum-Werte bleiben die
/// Source of Truth; nur die Anzeige läuft über <see cref="Localizer"/> (0.6.3).
/// </summary>
public static class EnumLabels
{
    public static string Key(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "DayMonday",
        DayOfWeek.Tuesday => "DayTuesday",
        DayOfWeek.Wednesday => "DayWednesday",
        DayOfWeek.Thursday => "DayThursday",
        DayOfWeek.Friday => "DayFriday",
        DayOfWeek.Saturday => "DaySaturday",
        DayOfWeek.Sunday => "DaySunday",
        _ => "DayMonday",
    };

    public static string Key(RoomControlMode mode) => mode switch
    {
        RoomControlMode.Auto => "ModeAuto",
        RoomControlMode.Permanently => "ModePermanent",
        RoomControlMode.Temporary => "ModeTemporary",
        _ => "ModeNotAvailable",
    };

    public static string Key(RoomTemperatureMode mode) => mode switch
    {
        RoomTemperatureMode.Eco => "ModeEco",
        RoomTemperatureMode.Comfort => "ModeComfort",
        RoomTemperatureMode.Normal => "ModeNormal",
        _ => "ModeNotAvailable",
    };

    public static string Key(DeviceType type) => type switch
    {
        DeviceType.HeatingThermostat => "DeviceHeatingThermostat",
        DeviceType.HeatingThermostatPlus => "DeviceHeatingThermostatPlus",
        DeviceType.WallMountedThermostat => "DeviceWallMountedThermostat",
        DeviceType.ShutterContact => "DeviceShutterContact",
        DeviceType.PushButton => "DevicePushButton",
        _ => "DeviceInvalid",
    };

    public static string Key(DeviceStateInfo state) => state switch
    {
        DeviceStateInfo.Valid => "StateValid",
        DeviceStateInfo.NotInitialized => "StateNotInitialized",
        DeviceStateInfo.OutOfDate => "StateOutOfDate",
        _ => "StateNotInitialized",
    };

    public static string Key(RadioState state) => state switch
    {
        RadioState.Ok => "RadioOk",
        RadioState.Error => "RadioError",
        RadioState.ControlFailed => "RadioControlFailed",
        RadioState.ConfigFailed => "RadioConfigFailed",
        RadioState.AssignFailed => "RadioAssignFailed",
        RadioState.DeleteFailed => "RadioDeleteFailed",
        _ => "RadioError",
    };

    public static string Key(PushButtonKeyMode mode) => mode switch
    {
        PushButtonKeyMode.Auto => "ModeAuto",
        PushButtonKeyMode.AutoWithTemperature => "PushButtonAutoWithTemperature",
        PushButtonKeyMode.Eco => "ModeEco",
        PushButtonKeyMode.Comfort => "ModeComfort",
        PushButtonKeyMode.Manual => "PushButtonManual",
        _ => "ModeNotAvailable",
    };

    public static string Key(string zonePreset) => zonePreset switch
    {
        "Mitteleuropa (CET/CEST)" => "ZoneCentralEurope",
        "Westeuropa (GMT/BST)" => "ZoneWesternEurope",
        "Osteuropa (EET/EEST)" => "ZoneEasternEurope",
        "Mitteleuropa ohne Sommerzeit" => "ZoneCentralEuropeNoDst",
        _ => "ZoneCentralEurope",
    };
}
