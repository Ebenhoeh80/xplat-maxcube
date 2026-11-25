using FluentAssertions;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using Xunit;

namespace MaxCube.Domain.Tests;

/// <summary>Gremium Runde 5: Raum-/Geräte-Strukturoperationen des Aggregats.</summary>
public class StructureTests
{
    private static readonly RadioAddress Heating1 = new(0x0A0B01);
    private static readonly RadioAddress Heating2 = new(0x0A0B02);
    private static readonly RadioAddress Wall = new(0x0A0B03);

    private static Cube Build()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", Heating1));
        cube.AddRoom(new Room(2, "Küche", Heating2));
        cube.AddDevice(new Device(Heating1, DeviceType.HeatingThermostat, 1, new SerialNumber("S1"), "H1", 1));
        cube.AddDevice(new Device(Heating2, DeviceType.HeatingThermostat, 1, new SerialNumber("S2"), "H2", 2));
        cube.AddDevice(new Device(Wall, DeviceType.WallMountedThermostat, 3, new SerialNumber("S3"), "Wand", 1));
        return cube;
    }

    [Fact]
    public void Rooms_and_devices_are_renamed_with_unique_case_insensitive_names()
    {
        var cube = Build();

        cube.RenameRoom(1, "Wohnzimmer");
        cube.RenameDevice(Wall, "Wandthermostat");

        cube.GetRoom(1)!.Name.Should().Be("Wohnzimmer");
        cube.GetDevice(Wall)!.Name.Should().Be("Wandthermostat");
        FluentActions.Invoking(() => cube.RenameRoom(1, "KÜCHE")).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => cube.RenameDevice(Wall, "h1")).Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.DeviceNameAlreadyExists);
        FluentActions.Invoking(() => cube.RenameRoom(1, new string('x', 33))).Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.NameTooLong);
        cube.RenameRoom(1, "wohnzimmer"); // eigener Name in anderer Schreibweise ist erlaubt
    }

    [Fact]
    public void Reordering_requires_every_room_exactly_once()
    {
        var cube = Build();

        cube.ReorderRooms([2, 1]);
        cube.Rooms.Select(room => room.Id).Should().Equal(2, 1);

        FluentActions.Invoking(() => cube.ReorderRooms([1])).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => cube.ReorderRooms([1, 1])).Should().Throw<DomainValidationException>();
        FluentActions.Invoking(() => cube.ReorderRooms([1, 9])).Should().Throw<DomainValidationException>();
        cube.Rooms.Select(room => room.Id).Should().Equal(2, 1);
    }

    [Fact]
    public void Removing_the_master_hands_the_role_to_the_next_thermostat()
    {
        var cube = Build();
        cube.AddDevice(new Device(new RadioAddress(0x0A0B04), DeviceType.HeatingThermostat, 1, new SerialNumber("S4"), "H1b", 1));

        cube.RemoveDevice(Heating1);

        cube.GetRoom(1)!.MasterRadioAddress.Should().Be(new RadioAddress(0x0A0B04));
        cube.GetDevice(Heating1).Should().BeNull();
    }

    [Fact]
    public void Removing_the_last_thermostat_clears_the_master_and_a_new_one_takes_over()
    {
        var cube = Build();

        cube.RemoveDevice(Heating1);
        cube.GetRoom(1)!.MasterRadioAddress.Value.Should().Be(0);

        cube.AddDevice(new Device(new RadioAddress(0x0A0B05), DeviceType.HeatingThermostat, 1, new SerialNumber("S5"), "Neu", 1));
        cube.EnsureRoomMaster(1);
        cube.GetRoom(1)!.MasterRadioAddress.Should().Be(new RadioAddress(0x0A0B05));
    }

    [Fact]
    public void A_room_can_only_be_removed_when_it_is_empty()
    {
        var cube = Build();

        FluentActions.Invoking(() => cube.RemoveRoom(1)).Should().Throw<DomainValidationException>();
        cube.RemoveDevice(Heating2);
        cube.RemoveRoom(2);

        cube.GetRoom(2).Should().BeNull();
        cube.NextFreeRoomId().Should().Be(2);
    }

    [Fact]
    public void Moving_to_the_house_and_back_keeps_both_lists_consistent()
    {
        var cube = Build();

        cube.MoveDevice(Wall, House.Id);
        cube.GetDevice(Wall)!.RoomId.Should().Be(House.Id);
        cube.House.Devices.Should().Contain(device => device.RadioAddress == Wall);
        cube.GetRoom(1)!.Devices.Should().NotContain(device => device.RadioAddress == Wall);

        cube.MoveDevice(Wall, 2);
        cube.GetDevice(Wall)!.RoomId.Should().Be(2);
        cube.House.Devices.Should().BeEmpty();
        cube.GetRoom(2)!.Devices.Should().Contain(device => device.RadioAddress == Wall);
    }

    [Fact]
    public void A_move_that_violates_a_limit_changes_nothing()
    {
        var cube = Build();
        cube.AddDevice(new Device(new RadioAddress(0x0A0B09), DeviceType.WallMountedThermostat, 3, new SerialNumber("S9"), "Wand Küche", 2));

        FluentActions.Invoking(() => cube.MoveDevice(Wall, 2)).Should().Throw<DomainValidationException>();

        cube.GetDevice(Wall)!.RoomId.Should().Be(1);
        cube.GetRoom(1)!.Devices.Should().Contain(device => device.RadioAddress == Wall);
        cube.GetRoom(2)!.Devices.Should().NotContain(device => device.RadioAddress == Wall);
    }

    [Fact]
    public void Moving_the_master_thermostat_passes_the_role_on_and_takes_it_in_an_empty_room()
    {
        var cube = Build();
        cube.AddRoom(new Room(3, "Bad", new RadioAddress(0)));
        cube.AddDevice(new Device(new RadioAddress(0x0A0B06), DeviceType.HeatingThermostat, 1, new SerialNumber("S6"), "H1c", 1));

        cube.MoveDevice(Heating1, 3);

        cube.GetRoom(1)!.MasterRadioAddress.Should().Be(new RadioAddress(0x0A0B06));
        cube.GetRoom(3)!.MasterRadioAddress.Should().Be(Heating1);
    }

    [Fact]
    public void Valve_configuration_is_only_known_after_it_was_read()
    {
        var cube = Build();
        var room = cube.GetRoom(1)!;

        room.HasValveConfiguration.Should().BeFalse();
        cube.ApplyValveConfiguration(1, 200, 51);

        room.HasValveConfiguration.Should().BeTrue();
        room.MaxValveRaw.Should().Be(200);
        room.ValveOffsetRaw.Should().Be(51);
    }
}
