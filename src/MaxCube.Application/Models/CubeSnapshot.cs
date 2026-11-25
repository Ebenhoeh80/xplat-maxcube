using System.Text.Json;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Application.Models;

/// <summary>
/// Abbild des Aggregats für den SQLite-Cache. Bewusst flach und ohne Domänenverhalten;
/// der Cube ist Source of Truth, der Snapshot dient nur der Offline-Anzeige.
/// </summary>
public sealed record CubeSnapshot(
    string SerialNumber,
    int FirmwareVersion,
    string RfAddress,
    bool DaylightSaving,
    DateTimeOffset CapturedAt,
    IReadOnlyList<RoomSnapshot> Rooms,
    IReadOnlyList<DeviceSnapshot> Devices)
{
    public static CubeSnapshot From(Cube cube, DateTimeOffset capturedAt) => new(
        cube.SerialNumber.Value,
        cube.FirmwareVersion,
        cube.RfAddress.Value.ToString("X6"),
        cube.DaylightSaving,
        capturedAt,
        cube.Rooms.Select(RoomSnapshot.From).ToArray(),
        cube.Devices.Select(DeviceSnapshot.From).ToArray());

    /// <summary>
    /// Rekonstruiert einen schreibbaren Cube aus dem Cache. Die Reihenfolge entspricht
    /// CubeStateAssembler: Räume, Geräte, Konfiguration, anschließend Telemetrie.
    /// </summary>
    public Cube ToCube()
    {
        var cube = new Cube(
            new SerialNumber(SerialNumber),
            FirmwareVersion,
            new RadioAddress(Convert.ToInt32(RfAddress, 16)),
            DaylightSaving);

        foreach (var room in Rooms)
        {
            cube.AddRoom(new Room(room.Id, room.Name, new RadioAddress(Convert.ToInt32(room.MasterAddress, 16))));
        }

        foreach (var device in Devices)
        {
            cube.AddDevice(new Device(
                new RadioAddress(Convert.ToInt32(device.Address, 16)),
                device.Type,
                device.NumericType,
                new SerialNumber(device.SerialNumber),
                device.Name,
                device.RoomId));
        }

        foreach (var room in Rooms)
        {
            cube.ApplyRoomConfiguration(
                room.Id,
                new Temperature(room.Comfort),
                new Temperature(room.Eco),
                new Temperature(room.Maximum),
                new Temperature(room.Minimum),
                room.Offset,
                new Temperature(room.WindowOpen),
                room.WindowOpenMinutes,
                room.BoostMinutes,
                room.BoostValve,
                (DayOfWeek)room.DecalcificationDay,
                room.DecalcificationHour);
        }

        foreach (var device in Devices)
        {
            cube.UpdateDeviceState(
                new RadioAddress(Convert.ToInt32(device.Address, 16)), device.StateInfo, device.RadioState);
        }

        // Die Telemetrie gehört zum Raum, nicht zum Gerät. Auch ein Raum ohne Geräte
        // muss seine Werte zurückbekommen, sonst verliert der Offline-Cache Soll/Ist.
        foreach (var room in Rooms)
        {
            cube.SetRoomTemperature(room.Id, new Temperature(room.SetPoint));
            cube.SetRoomActualTemperature(room.Id, new MeasuredTemperature(Math.Clamp(room.Actual, 0, 40)));
            cube.SetControlMode(room.Id, room.ControlMode);
            cube.SetTemperatureMode(room.Id, room.TemperatureMode);

            if (!string.IsNullOrWhiteSpace(room.WeekProfile))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<WeekProfileSnapshot>(room.WeekProfile);
                    if (profile is not null)
                    {
                        cube.ApplyWeekProfile(room.Id, profile.ToProfile());
                    }
                }
                catch (Exception exception) when (exception is JsonException or DomainValidationException)
                {
                    // Ein korruptes Cache-Profil bleibt ungeladen; der Editor sperrt das
                    // Senden dann, statt einen Defaultplan zum Cube zu schicken.
                }
            }
        }

        return cube;
    }
}

public sealed record RoomSnapshot(
    int Id,
    string Name,
    string MasterAddress,
    double SetPoint,
    double Actual,
    RoomControlMode ControlMode,
    RoomTemperatureMode TemperatureMode,
    double Comfort,
    double Eco,
    double Maximum,
    double Minimum,
    double WindowOpen,
    int WindowOpenMinutes,
    double Offset,
    int BoostMinutes,
    int BoostValve,
    int DecalcificationDay,
    int DecalcificationHour,
    string? WeekProfile = null)
{
    public static RoomSnapshot From(Room room) => new(
        room.Id,
        room.Name,
        room.MasterRadioAddress.Value.ToString("X6"),
        room.SetPointTemperature.Value,
        room.ActualTemperature.Value,
        room.ControlMode,
        room.TemperatureMode,
        room.ComfortTemperature.Value,
        room.EcoTemperature.Value,
        room.MaximumTemperature.Value,
        room.MinimumTemperature.Value,
        room.WindowOpenTemperature.Value,
        room.WindowOpenDurationMinutes,
        room.TemperatureOffset,
        room.BoostDurationMinutes,
        room.BoostValveAngle,
        (int)room.DecalcificationDay,
        room.DecalcificationHour,
        WeekProfileSnapshot.From(room.WeekProfile) is { } profile
            ? JsonSerializer.Serialize(profile)
            : null);
}

/// <summary>Flaches, versionsstabiles Cache-Format für das Wochenprofil (Minuten ab 0:00,
/// 1440 = 24:00, Temperatur als Drahtwert in 0,5 °C). Der Cube bleibt Source of Truth.</summary>
public sealed record WeekProfileSnapshot(Dictionary<int, List<SwitchPointSnapshot>> Days)
{
    public static WeekProfileSnapshot? From(WeekTemperatureProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new WeekProfileSnapshot(profile.Days.ToDictionary(
            day => (int)day.Day,
            day => day.Points.Select(point => new SwitchPointSnapshot(
                point.Until == TimeOnly.MinValue
                    ? 1440
                    : (point.Until.Hour * 60) + point.Until.Minute,
                point.Temperature.ToWire())).ToList()));
    }

    public WeekTemperatureProfile ToProfile()
    {
        var result = new WeekTemperatureProfile();
        foreach (var (day, points) in Days)
        {
            result.SetDay(new DayProfile((DayOfWeek)day, points.Select(point => new SwitchPoint(
                point.MinuteOfDay == 1440
                    ? TimeOnly.MinValue
                    : new TimeOnly(point.MinuteOfDay / 60, point.MinuteOfDay % 60),
                Temperature.FromWire(point.HalfDegrees))).ToArray()));
        }

        return result;
    }
}

public sealed record SwitchPointSnapshot(int MinuteOfDay, int HalfDegrees);

public sealed record DeviceSnapshot(
    string Address,
    DeviceType Type,
    int NumericType,
    string SerialNumber,
    string Name,
    int RoomId,
    DeviceStateInfo StateInfo,
    RadioState RadioState)
{
    public static DeviceSnapshot From(Device device) => new(
        device.RadioAddress.Value.ToString("X6"),
        device.DeviceType,
        device.NumericDeviceType,
        device.SerialNumber.Value,
        device.Name,
        device.RoomId,
        device.StateInfo,
        device.RadioState);
}
