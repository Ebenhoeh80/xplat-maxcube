using FluentAssertions;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Events;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Domain.Tests;

public class AggregateTests
{
    private static Cube NewCube() => new(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);

    private static Device NewDevice(int address, DeviceType type = DeviceType.HeatingThermostat, int roomId = 1)
        => NewDevice(address, type, roomId, $"Device{address}");

    private static Device NewDevice(int address, DeviceType type, int roomId, string? name)
        => new(new RadioAddress(address), type, (int)type, new SerialNumber("JEQ0001234"), name!, roomId);

    [Fact]
    public void Defaults_follow_the_java_room_and_device_constructors()
    {
        var room = new Room(1, "Wohnen", new RadioAddress(0x123456));

        room.EcoTemperature.Value.Should().Be(16.5);
        room.ComfortTemperature.Value.Should().Be(21.5);
        room.MaximumTemperature.Value.Should().Be(30.5);
        room.WindowOpenTemperature.Value.Should().Be(12.0);
        room.WindowOpenDurationMinutes.Should().Be(15);
        room.BoostDurationMinutes.Should().Be(5);
        room.BoostValveAngle.Should().Be(80);
        room.DecalcificationDay.Should().Be(DayOfWeek.Saturday);
        room.DecalcificationHour.Should().Be(12);
        room.ControlMode.Should().Be(RoomControlMode.Auto);
        room.TemperatureMode.Should().Be(RoomTemperatureMode.NotAvailable);

        var device = NewDevice(0x0A0B0C);
        device.StateInfo.Should().Be(DeviceStateInfo.NotInitialized);
        device.RadioState.Should().Be(RadioState.Ok);
    }

    [Fact]
    public void Devices_are_routed_to_their_room_or_to_the_house()
    {
        var cube = NewCube();
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0x123456)));

        cube.AddDevice(NewDevice(0x0A0B0C, roomId: 1));
        cube.AddDevice(NewDevice(0x0A0B0D, roomId: 0));

        cube.GetRoom(1)!.Devices.Should().ContainSingle();
        cube.House.Devices.Should().ContainSingle();
        cube.Devices.Should().HaveCount(2);
        cube.GetRoom(1)!.GetDevice(new RadioAddress(0x0A0B0C))!.RoomId.Should().Be(1);
        cube.House.GetDevice(new RadioAddress(0x0A0B0D))!.RoomId.Should().Be(0);
    }

    [Fact]
    public void The_21st_room_is_rejected()
    {
        var cube = NewCube();
        for (var id = 1; id <= 20; id++)
        {
            cube.AddRoom(new Room(id, $"Raum{id}", new RadioAddress(0)));
        }

        var act = () => cube.AddRoom(new Room(21, "ZuViel", new RadioAddress(0)));

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.Validation);
        cube.Rooms.Should().HaveCount(20);
    }

    [Fact]
    public void The_141st_device_is_rejected()
    {
        var cube = NewCube();
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0)));
        for (var index = 0; index < 140; index++)
        {
            cube.AddDevice(NewDevice(0x010000 + index, roomId: 0));
        }

        var act = () => cube.AddDevice(NewDevice(0x020000, roomId: 0));

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.Validation);
        cube.Devices.Should().HaveCount(140);
    }

    [Theory]
    [InlineData(null, "MAX1029")]
    [InlineData("123456789012345678901234567890123", "MAX1030")]
    public void Names_are_validated(string? name, string expectedCode)
    {
        var act = () => NewDevice(0x0A0B0C, DeviceType.HeatingThermostat, 1, name);

        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Duplicate_radio_addresses_are_rejected()
    {
        var cube = NewCube();
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0)));
        cube.AddDevice(NewDevice(0x0A0B0C));

        var act = () => cube.AddDevice(NewDevice(0x0A0B0C));

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.InvalidDeviceRadioAddress);
    }

    [Fact]
    public void Room_type_limits_are_enforced()
    {
        var cube = NewCube();
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0)));
        cube.AddDevice(NewDevice(0x0A0B0C, DeviceType.WallMountedThermostat));

        var act = () => cube.AddDevice(NewDevice(0x0A0B0D, DeviceType.WallMountedThermostat));

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.Validation);
    }

    [Fact]
    public void State_changes_raise_domain_events_and_are_drained()
    {
        var cube = NewCube();
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0)));
        cube.AddDevice(NewDevice(0x0A0B0C));

        cube.SetRoomTemperature(1, new Temperature(21.5));
        cube.SetControlMode(1, RoomControlMode.Temporary);
        cube.UpdateDeviceState(new RadioAddress(0x0A0B0C), DeviceStateInfo.Valid, RadioState.Ok);

        cube.TakeDomainEvents().Should().Equal(
            new RoomStateChanged(1, "SetPointTemperature"),
            new RoomStateChanged(1, "ControlMode"),
            new DeviceStateChanged(new RadioAddress(0x0A0B0C), "State"));
        cube.TakeDomainEvents().Should().BeEmpty();
        cube.GetRoom(1)!.SetPointTemperature.Should().Be(new Temperature(21.5));
        cube.GetDevice(new RadioAddress(0x0A0B0C))!.StateInfo.Should().Be(DeviceStateInfo.Valid);
    }
}
