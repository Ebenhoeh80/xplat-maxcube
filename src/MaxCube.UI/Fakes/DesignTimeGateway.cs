using MaxCube.Application.Abstractions;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.UI.Fakes;

/// <summary>Design-Time-Daten für die XAML-Vorschau; keine echte Verbindung.</summary>
public sealed class DesignTimeGateway : ICubeGateway
{
    public CubeConnectionState State => CubeConnectionState.Disconnected;
    public event EventHandler<CubeConnectionState>? StateChanged { add { } remove { } }
    public event EventHandler<Cube>? CubeRefreshed { add { } remove { } }
    public event EventHandler? SessionUnreachable { add { } remove { } }

    public Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCube());

    public Task SetRoomTemperatureAsync(
        int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetRoomControlModeAsync(
        int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
        DateTimeOffset? until, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetWeekDayProfileAsync(
        int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public static Cube CreateCube()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        var room = new Room(1, "Wohnen", new RadioAddress(0x1D2A3C));
        var device = new Device(
            new RadioAddress(0x0A0B0C), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0001234"), "Wohnen Thermostat", 1);
        cube.AddRoom(room);
        cube.AddDevice(device);
        cube.ApplyDeviceTelemetry(device.RadioAddress,
            DeviceStateInfo.Valid, RadioState.Ok, new Temperature(21.5));
        return cube;
    }
}
