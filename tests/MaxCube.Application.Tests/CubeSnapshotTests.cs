using FluentAssertions;
using MaxCube.Application.Models;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using Xunit;

namespace MaxCube.Application.Tests;

public class CubeSnapshotTests
{
    [Fact]
    public void Roundtrip_preserves_room_telemetry_even_without_devices()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0x1D2A3C)));
        cube.SetRoomTemperature(1, new Temperature(21.5));
        cube.SetRoomActualTemperature(1, new MeasuredTemperature(19.0));
        cube.SetControlMode(1, RoomControlMode.Permanently);
        cube.SetTemperatureMode(1, RoomTemperatureMode.Comfort);

        var restored = CubeSnapshot.From(cube, new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)).ToCube();

        var room = restored.GetRoom(1)!;
        room.SetPointTemperature.Should().Be(new Temperature(21.5));
        room.ActualTemperature.Should().Be(new MeasuredTemperature(19.0));
        room.ControlMode.Should().Be(RoomControlMode.Permanently);
        room.TemperatureMode.Should().Be(RoomTemperatureMode.Comfort);
    }
}
