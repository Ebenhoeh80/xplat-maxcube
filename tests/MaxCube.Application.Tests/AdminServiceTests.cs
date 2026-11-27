using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Application.Tests;

/// <summary>Gremium Runde 5: erweiterte Konfiguration, Anlernen und Strukturpflege.</summary>
public class AdminServiceTests
{
    private static readonly RadioAddress Heating = new(0x0A0B0C);
    private static readonly RadioAddress Wall = new(0x0A0B0D);
    private static readonly RadioAddress Shutter = new(0x0A0B0E);

    private static Cube BuildCube(bool withValve = true)
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", Heating));
        cube.AddRoom(new Room(2, "Küche", new RadioAddress(0x0A0B20)));
        cube.AddDevice(new Device(Heating, DeviceType.HeatingThermostat, 1, new SerialNumber("JEQ0000001"), "Heizung Wohnen", 1));
        cube.AddDevice(new Device(Wall, DeviceType.WallMountedThermostat, 3, new SerialNumber("JEQ0000002"), "Wand Wohnen", 1));
        cube.AddDevice(new Device(
            new RadioAddress(0x0A0B20), DeviceType.HeatingThermostat, 1, new SerialNumber("JEQ0000004"), "Heizung Küche", 2));
        if (withValve)
        {
            cube.ApplyRoomConfiguration(
                1, new Temperature(21.5), new Temperature(16.5), new Temperature(30.5), new Temperature(4.5),
                0, new Temperature(12.0), 15, 15, 80, DayOfWeek.Saturday, 12);
            cube.ApplyValveConfiguration(1, 255, 0);
        }

        return cube;
    }

    private sealed class Harness
    {
        public Harness(Cube? cube = null)
        {
            Cube = cube ?? BuildCube();
            Core = new FakeCore { CubeToReturn = Cube };
            Store = new FakeStore();
            Admin = new FakeAdmin();
            Service = new CubeApplicationService(Core, Store, new FakeClock(), admin: Admin);
        }

        public Cube Cube { get; }
        public FakeCore Core { get; }
        public FakeStore Store { get; }
        public FakeAdmin Admin { get; }
        public CubeApplicationService Service { get; }

        public async Task<CubeApplicationService> ConnectAsync()
        {
            await Service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));
            return Service;
        }
    }

    [Fact]
    public async Task Room_configuration_is_quantised_sent_and_applied()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.SetRoomConfigurationAsync(1, new RoomConfigurationValues(
            new Temperature(21.3), new Temperature(17.0), new Temperature(28.0), new Temperature(6.0),
            0.7, new Temperature(10.0), 10));

        var sent = h.Admin.Configurations.Should().ContainSingle().Subject;
        sent.Comfort.Value.Should().Be(21.5);
        sent.Offset.Should().Be(0.5);
        var room = h.Cube.GetRoom(1)!;
        room.ComfortTemperature.Value.Should().Be(21.5);
        room.MaximumTemperature.Value.Should().Be(28.0);
        room.WindowOpenDurationMinutes.Should().Be(10);
        room.BoostDurationMinutes.Should().Be(15, "Boost bleibt von 0x11 unberührt");
        h.Store.Audit.Should().Contain(text => text.Contains("Raumkonfiguration 1"));
    }

    [Theory]
    [InlineData(21.5, 28.0, 25.0, 4.5, 0.0, 15)]  // ECO über Maximum
    [InlineData(21.5, 16.5, 10.0, 12.0, 0.0, 15)] // Minimum über Maximum
    [InlineData(21.5, 16.5, 30.5, 4.5, 4.0, 15)]  // Offset außerhalb
    [InlineData(21.5, 16.5, 30.5, 4.5, 0.0, 45)]  // Fensterdauer außerhalb
    [InlineData(21.5, 16.5, 30.5, 4.5, 0.0, 7)]   // kein 5er-Schritt
    public async Task Invalid_room_configuration_sends_nothing(
        double comfort, double eco, double maximum, double minimum, double offset, int windowMinutes)
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var act = () => service.SetRoomConfigurationAsync(1, new RoomConfigurationValues(
            new Temperature(comfort), new Temperature(eco), new Temperature(maximum), new Temperature(minimum),
            offset, new Temperature(12.0), windowMinutes));

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Configurations.Should().BeEmpty();
    }

    [Fact]
    public async Task Boost_and_decalcification_pass_the_valve_raw_values_through()
    {
        var cube = BuildCube();
        cube.ApplyValveConfiguration(1, 200, 51);
        var h = new Harness(cube);
        var service = await h.ConnectAsync();

        await service.SetBoostAndDecalcificationAsync(1, 30, 60, DayOfWeek.Monday, 6);

        var sent = h.Admin.ValveFunctions.Should().ContainSingle().Subject;
        sent.BoostMinutes.Should().Be(30);
        sent.BoostValveAngle.Should().Be(60);
        sent.DecalcificationDay.Should().Be(DayOfWeek.Monday);
        sent.MaxValveRaw.Should().Be(200);
        sent.ValveOffsetRaw.Should().Be(51);
        var room = h.Cube.GetRoom(1)!;
        room.BoostDurationMinutes.Should().Be(30);
        room.DecalcificationHour.Should().Be(6);
        room.MaxValveRaw.Should().Be(200);
    }

    [Fact]
    public async Task Boost_is_blocked_when_the_valve_values_were_never_read()
    {
        var h = new Harness(BuildCube(withValve: false));
        var service = await h.ConnectAsync();

        var act = () => service.SetBoostAndDecalcificationAsync(1, 15, 80, DayOfWeek.Saturday, 12);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*nicht vom Cube gelesen*");
        h.Admin.ValveFunctions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(7, 80, 12)]
    [InlineData(15, 82, 12)]
    [InlineData(15, 80, 24)]
    [InlineData(15, 105, 12)]
    public async Task Invalid_boost_or_decalcification_values_are_rejected(int boost, int valve, int hour)
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var act = () => service.SetBoostAndDecalcificationAsync(1, boost, valve, DayOfWeek.Saturday, hour);

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.ValveFunctions.Should().BeEmpty();
    }

    [Fact]
    public async Task Valve_offset_percent_is_converted_and_other_values_are_kept()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.SetValveOffsetAsync(1, 20);

        var sent = h.Admin.ValveFunctions.Should().ContainSingle().Subject;
        sent.ValveOffsetRaw.Should().Be(51);
        sent.BoostMinutes.Should().Be(15);
        CubeApplicationService.ValveOffsetToPercent(51).Should().Be(20);
    }

    [Fact]
    public async Task Display_mode_is_only_allowed_for_wall_thermostats()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.SetWallThermostatDisplayAsync(Wall, showActualTemperature: true);
        var act = () => service.SetWallThermostatDisplayAsync(Heating, true);

        h.Admin.Displays.Should().ContainSingle().Which.Should().Be((Wall, true));
        await act.Should().ThrowAsync<DomainValidationException>();
    }

    [Fact]
    public async Task Bundled_temperatures_send_only_the_last_value_per_room()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        service.QueueRoomTemperature(1, new Temperature(19.0));
        service.QueueRoomTemperature(1, new Temperature(20.5));
        service.QueueRoomTemperature(2, new Temperature(18.0));
        service.PendingTemperatureCount.Should().Be(2);
        h.Core.Calls.Should().NotContain(call => call.StartsWith("temperature"));

        var sent = await service.FlushPendingTemperaturesAsync();

        sent.Should().Be(2);
        h.Core.Calls.Count(call => call == "temperature:1").Should().Be(1);
        h.Core.LastTemperatures[1].Value.Should().Be(20.5);
        h.Core.LastTemperatures[2].Value.Should().Be(18.0);
        service.PendingTemperatureCount.Should().Be(0);
    }

    [Fact]
    public async Task Search_returns_a_new_device_and_ignores_known_ones()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        h.Admin.Found.Enqueue(new FoundDevice(DeviceType.HeatingThermostat, 1, Heating, "JEQ0000001")); // bekannt
        h.Admin.Found.Enqueue(new FoundDevice(DeviceType.ShutterContact, 4, Shutter, "JEQ0000009"));

        var found = await service.SearchNewDeviceAsync(TimeSpan.FromSeconds(30));

        found.Should().NotBeNull();
        found!.Address.Should().Be(Shutter);
        h.Admin.Calls.Should().Contain("endInclusion");
        h.Store.Audit.Should().Contain(text => text.Contains("Neues Gerät gefunden"));
    }

    [Fact]
    public async Task Search_without_a_device_times_out_with_null()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var found = await service.SearchNewDeviceAsync(TimeSpan.FromMilliseconds(50));

        found.Should().BeNull();
        h.Admin.Calls.Should().Contain("endInclusion");
    }

    [Fact]
    public async Task Assigning_a_shutter_contact_sets_the_group_links_it_and_writes_metadata()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var found = new FoundDevice(DeviceType.ShutterContact, 4, Shutter, "JEQ0000009");

        await service.AssignNewDeviceAsync(found, "Fenster Wohnen", 1, null, confirmed: true);

        h.Admin.Calls.Should().ContainInOrder(
            $"group:{Shutter}:1",
            $"linkroom:1:{Heating}->{Shutter}:add",
            $"link:{Shutter}->{Heating}:add",
            $"link:{Heating}->{Shutter}:add",
            "metadata");
        h.Admin.Metadata.Should().ContainSingle().Which.DeviceNames.Should().Contain("Fenster Wohnen");
        h.Cube.GetDevice(Shutter)!.RoomId.Should().Be(1);
        h.Cube.GetRoom(1)!.Devices.Should().Contain(device => device.RadioAddress == Shutter);
    }

    [Fact]
    public async Task Assigning_requires_confirmation_and_sends_nothing_otherwise()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var found = new FoundDevice(DeviceType.ShutterContact, 4, Shutter, "JEQ0000009");

        var act = () => service.AssignNewDeviceAsync(found, "Fenster", 1, null, confirmed: false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.Admin.Calls.Should().NotContain(call => call.StartsWith("group") || call == "metadata");
    }

    [Fact]
    public async Task A_new_room_needs_a_heating_thermostat_as_master()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var wallInNewRoom = () => service.AssignNewDeviceAsync(
            new FoundDevice(DeviceType.WallMountedThermostat, 3, Shutter, "JEQ0000009"), "Wand", 0, "Bad", true);
        await wallInNewRoom.Should().ThrowAsync<DomainValidationException>();

        var thermostatInNewRoom = new FoundDevice(DeviceType.HeatingThermostat, 1, new RadioAddress(0x0A0B30), "JEQ0000010");
        await service.AssignNewDeviceAsync(thermostatInNewRoom, "Heizung Bad", 0, "Bad", true);

        var room = h.Cube.Rooms.Should().Contain(r => r.Name == "Bad").Subject;
        room.Id.Should().Be(3);
        room.MasterRadioAddress.Should().Be(new RadioAddress(0x0A0B30));
        h.Admin.Calls.Should().Contain($"group:{new RadioAddress(0x0A0B30)}:3");
    }

    [Fact]
    public async Task Duplicate_names_are_rejected_before_anything_is_sent()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var found = new FoundDevice(DeviceType.ShutterContact, 4, Shutter, "JEQ0000009");

        var act = () => service.AssignNewDeviceAsync(found, "HEIZUNG WOHNEN", 1, null, true);

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Calls.Should().BeEmpty();
        h.Cube.GetDevice(Shutter).Should().BeNull();
    }

    [Fact]
    public async Task Rename_room_writes_metadata_then_changes_the_model()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.RenameRoomAsync(1, "  Wohnzimmer ");

        h.Admin.Metadata.Should().ContainSingle().Which.RoomNames.Should().Contain("Wohnzimmer");
        h.Cube.GetRoom(1)!.Name.Should().Be("Wohnzimmer");
    }

    [Fact]
    public async Task A_failed_metadata_write_leaves_the_model_untouched_and_is_audited()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        h.Admin.FailMetadata = true;

        var act = () => service.RenameRoomAsync(1, "Wohnzimmer");

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.Cube.GetRoom(1)!.Name.Should().Be("Wohnen");
        h.Store.Audit.Should().Contain(text =>
            text.StartsWith("[system] ") && text.Contains("Strukturänderung fehlgeschlagen"));
    }

    [Fact]
    public async Task Duplicate_room_names_are_rejected_without_sending()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var act = () => service.RenameRoomAsync(1, "KÜCHE");

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task Rooms_can_be_reordered_and_the_order_is_sent()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.ReorderRoomsAsync([2, 1]);

        h.Cube.Rooms.Select(room => room.Id).Should().Equal(2, 1);
        h.Admin.Metadata.Single().RoomNames.Should().Equal("Küche", "Wohnen");
    }

    [Fact]
    public async Task Deleting_a_device_needs_confirmation_and_removes_it_everywhere()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var unconfirmed = () => service.DeleteDeviceAsync(Wall, false, confirmed: false);
        await unconfirmed.Should().ThrowAsync<InvalidOperationException>();

        await service.DeleteDeviceAsync(Wall, factoryReset: true, confirmed: true);

        h.Admin.Calls.Should().ContainInOrder($"delete:{Wall}:force", "metadata");
        h.Cube.GetDevice(Wall).Should().BeNull();
        h.Cube.GetRoom(1)!.Devices.Should().NotContain(device => device.RadioAddress == Wall);
    }

    [Fact]
    public async Task The_only_thermostat_of_a_room_with_other_devices_cannot_be_deleted()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var act = () => service.DeleteDeviceAsync(Heating, false, true);

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_room_deletes_all_its_devices_first()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.DeleteRoomAsync(1, factoryReset: false, confirmed: true);

        h.Admin.Calls.Should().ContainInOrder($"delete:{Heating},{Wall}:soft", "metadata");
        h.Cube.GetRoom(1).Should().BeNull();
        h.Cube.Devices.Select(device => device.RadioAddress).Should().NotContain([Heating, Wall]);
    }

    [Fact]
    public async Task Moving_a_device_unlinks_the_old_room_and_links_the_new_one()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var hint = await service.MoveDeviceAsync(Wall, 2, confirmed: true);

        hint.Should().BeNull();
        h.Admin.Calls.Should().ContainInOrder(
            $"linkroom:1:{Heating}->{Wall}:remove",
            $"link:{Wall}->{Heating}:remove",
            $"link:{Heating}->{Wall}:remove",
            $"group:{Wall}:2",
            "metadata");
        h.Admin.Calls.Should().Contain($"link:{Wall}->{new RadioAddress(0x0A0B20)}:add");
        h.Cube.GetDevice(Wall)!.RoomId.Should().Be(2);
    }

    [Fact]
    public async Task Moving_a_thermostat_warns_about_the_mode_and_a_move_to_the_house_removes_the_group()
    {
        var cube = BuildCube();
        cube.AddDevice(new Device(new RadioAddress(0x0A0B21), DeviceType.HeatingThermostat, 1, new SerialNumber("JEQ0000005"), "Zweite Küche", 2));
        var h = new Harness(cube);
        var service = await h.ConnectAsync();

        var hint = await service.MoveDeviceAsync(new RadioAddress(0x0A0B21), 1, confirmed: true);
        await service.MoveDeviceAsync(Wall, 0, confirmed: true);

        hint.Should().Contain("Modus");
        h.Admin.Calls.Should().Contain($"removeGroup:{Wall}:1");
        h.Cube.GetDevice(Wall)!.RoomId.Should().Be(House.Id);
    }

    [Fact]
    public async Task Moving_into_a_full_room_is_rejected_before_any_telegram()
    {
        var cube = BuildCube();
        for (var index = 0; index < 7; index++)
        {
            cube.AddDevice(new Device(
                new RadioAddress(0x0B0000 + index), DeviceType.HeatingThermostat, 1,
                new SerialNumber($"JEQ00001{index}"), $"Extra {index}", 1));
        }

        var kitchen = new RadioAddress(0x0A0B20);
        var h = new Harness(cube);
        var service = await h.ConnectAsync();

        var act = () => service.MoveDeviceAsync(kitchen, 1, confirmed: true);

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Calls.Should().BeEmpty();
        h.Cube.GetDevice(kitchen)!.RoomId.Should().Be(2);
    }

    [Fact]
    public async Task Push_button_and_time_settings_are_passed_to_the_cube_with_validation()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var valid = new PushButtonConfiguration(
            new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, new Temperature(22.0)),
            new PushButtonKeyConfiguration(PushButtonKeyMode.Eco, null));

        await service.SetPushButtonAsync(valid);
        var invalid = () => service.SetPushButtonAsync(new PushButtonConfiguration(
            new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, null), valid.Lower));
        await service.SetTimeZoneAsync(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);
        await service.SetCubeTimeAsync();

        h.Admin.PushButtons.Should().ContainSingle().Which.Should().Be(valid);
        await invalid.Should().ThrowAsync<DomainValidationException>();
        h.Admin.TimeZones.Should().HaveCount(2);
        h.Admin.TimeZones[0].Time.Should().BeNull();
        h.Admin.TimeZones[1].Time.Should().Be(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
        h.Cube.DaylightSaving.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("pool,ntp.org")]
    [InlineData("pool ntp.org")]
    public async Task Invalid_ntp_servers_are_rejected(string server)
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        var act = () => service.SetNtpServersAsync([server]);

        await act.Should().ThrowAsync<DomainValidationException>();
        h.Admin.Ntp.Should().BeEmpty();
    }

    [Fact]
    public async Task Ntp_servers_are_trimmed_and_limited_to_two()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.SetNtpServersAsync([" pool.ntp.org ", "time.eq-3.de"]);
        var tooMany = () => service.SetNtpServersAsync(["a.b", "c.d", "e.f"]);

        h.Admin.Ntp.Should().ContainSingle().Which.Should().Equal("pool.ntp.org", "time.eq-3.de");
        await tooMany.Should().ThrowAsync<DomainValidationException>();
    }

    [Theory]
    [InlineData("192.168.178.3", true)]
    [InlineData("127.1", false)]
    [InlineData("256.0.0.1", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("", false)]
    [InlineData("1.2.3.-4", false)]
    public void Network_addresses_need_four_decimal_octets(string value, bool expected)
    {
        CubeApplicationService.IsStrictIPv4(value).Should().Be(expected);
    }

    [Fact]
    public async Task Writing_the_network_configuration_requires_confirmation_and_valid_fields()
    {
        var h = new Harness();
        var network = new FakeNetwork();
        var service = new CubeApplicationService(h.Core, h.Store, new FakeClock(), admin: h.Admin, network: network);
        await service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));
        var valid = new CubeNetworkConfiguration("192.168.1.5", "255.255.255.0", "192.168.1.1", "192.168.1.1", "8.8.8.8", false);

        var unconfirmed = () => service.WriteNetworkConfigurationAsync(valid, confirmed: false);
        var badField = () => service.WriteNetworkConfigurationAsync(valid with { Gateway = "1.2.3" }, confirmed: true);
        await service.WriteNetworkConfigurationAsync(valid, confirmed: true);

        await unconfirmed.Should().ThrowAsync<InvalidOperationException>();
        await badField.Should().ThrowAsync<DomainValidationException>();
        network.Written.Should().ContainSingle().Which.Should().Be(("JEQ0001234", "192.168.1.5", valid));
    }

    [Fact]
    public async Task Network_configuration_write_is_blocked_for_the_offline_cache()
    {
        var h = new Harness();
        var network = new FakeNetwork();
        var service = new CubeApplicationService(h.Core, h.Store, new FakeClock(), admin: h.Admin, network: network);
        await service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));
        h.Store.Settings["cube.serial"] = "JEQ0001234";
        h.Store.Snapshot = CubeSnapshot.From(h.Cube, DateTimeOffset.UnixEpoch);
        await service.DisconnectAsync();
        await service.LoadCachedAsync();
        var valid = new CubeNetworkConfiguration("192.168.1.5", "255.255.255.0", "192.168.1.1", "192.168.1.1", "8.8.8.8", false);

        var act = () => service.WriteNetworkConfigurationAsync(valid, confirmed: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        network.Written.Should().BeEmpty();
        h.Store.Audit.Should().NotContain(entry => entry.Contains("Netzwerkkonfiguration"));
    }

    [Fact]
    public async Task Network_configuration_write_honours_cancel_active_operation()
    {
        var h = new Harness();
        var network = new FakeNetwork();
        var service = new CubeApplicationService(h.Core, h.Store, new FakeClock(), admin: h.Admin, network: network);
        await service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        network.WriteHook = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var valid = new CubeNetworkConfiguration("192.168.1.5", "255.255.255.0", "192.168.1.1", "192.168.1.1", "8.8.8.8", false);
        var write = () => service.WriteNetworkConfigurationAsync(valid, confirmed: true);
        var running = write();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        service.CancelActiveOperation();

        Func<Task> runningCheck = () => running;
        await runningCheck.Should().ThrowAsync<OperationCanceledException>();
        network.Written.Should().BeEmpty();
        h.Store.Audit.Should().NotContain(entry => entry.Contains("Netzwerkkonfiguration"));
    }

    [Fact]
    public async Task Airtime_is_forwarded_and_a_high_duty_cycle_warns()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var warnings = new List<string>();
        service.Warning += (_, text) => warnings.Add(text);

        h.Admin.RaiseAirtime(new AirtimeStatus(9, 47));
        service.LastAirtime.Should().Be(new AirtimeStatus(9, 47));
        warnings.Should().BeEmpty();

        h.Admin.RaiseAirtime(new AirtimeStatus(85, 3));
        warnings.Should().ContainSingle().Which.Should().Contain("Funkauslastung");
    }

    [Fact]
    public async Task Admin_functions_are_blocked_for_the_offline_cache()
    {
        var h = new Harness();
        h.Store.Settings["cube.serial"] = "JEQ0001234";
        h.Store.Snapshot = CubeSnapshot.From(h.Cube, DateTimeOffset.UnixEpoch);
        var service = h.Service;
        await service.LoadCachedAsync();

        var act = () => service.RenameRoomAsync(1, "Neu");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        h.Admin.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_an_admin_gateway_the_extended_functions_explain_themselves()
    {
        var core = new FakeCore { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(core, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));

        var act = () => service.RenameRoomAsync(1, "Neu");

        service.SupportsAdministration.Should().BeFalse();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*erweiterten Funktionen*");
    }

    [Fact]
    public async Task The_diagnostic_report_contains_facts_but_no_secrets()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        h.Admin.RaiseAirtime(new AirtimeStatus(9, 47));

        var report = await service.BuildDiagnosticReportAsync("0.3.0");

        report.Should().Contain("App-Version: 0.3.0").And.Contain("JEQ0001234").And.Contain("Firmware 275 (unterstützt)")
            .And.Contain("Funkauslastung: 9").And.Contain("Räume: 2, Geräte: 3");
    }

    [Fact]
    public async Task Audit_cleanup_clamps_the_retention_window()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        await service.CleanupAuditAsync(0);
        h.Store.DeletedBefore.Should().Be(now.AddDays(-1));

        await service.CleanupAuditAsync(9999);
        h.Store.DeletedBefore.Should().Be(now.AddDays(-365));
    }

    [Fact]
    public async Task Reload_reconnects_to_the_last_endpoint()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.ReloadAsync();

        h.Core.Calls.Count(call => call == "connect:192.168.1.5:62910").Should().Be(2);
    }

    [Fact]
    public async Task Wake_up_room_requires_confirmation_and_audits_the_send()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.WakeUpRoomAsync(1, false, 45));
        h.Admin.WakeUps.Should().BeEmpty();

        await service.WakeUpRoomAsync(1, true, 45);

        h.Admin.WakeUps.Should().ContainSingle().Which.Should().Be(("room", 1, 45));
        h.Store.Audit.Should().Contain(text => text.Contains("Wake-up Raum 1"));
    }

    [Fact]
    public async Task Wake_up_device_is_sent_with_the_full_24_bit_address()
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await service.WakeUpDeviceAsync(Wall, true);

        h.Admin.WakeUps.Should().ContainSingle().Which.Should().Be(("device", 0x0A0B0D, 30));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public async Task Wake_up_rejects_durations_outside_the_protocol_field(int minutes)
    {
        var h = new Harness();
        var service = await h.ConnectAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() => service.WakeUpRoomAsync(1, true, minutes));
        h.Admin.WakeUps.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCore : ICubeGateway
    {
        public Cube? CubeToReturn { get; set; }
        public List<string> Calls { get; } = new();
        public Dictionary<int, Temperature> LastTemperatures { get; } = new();
        public CubeConnectionState State { get; private set; } = CubeConnectionState.Disconnected;

        public event EventHandler<CubeConnectionState>? StateChanged;
        public event EventHandler<Cube>? CubeRefreshed { add { } remove { } }
        public event EventHandler? SessionUnreachable { add { } remove { } }

        public Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            Calls.Add($"connect:{endpoint.Host}:{endpoint.Port}");
            State = CubeConnectionState.Connected;
            StateChanged?.Invoke(this, State);
            return Task.FromResult(CubeToReturn!);
        }

        public Task SetRoomTemperatureAsync(
            int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default)
        {
            Calls.Add($"temperature:{roomId}");
            LastTemperatures[roomId] = setPoint;
            return Task.CompletedTask;
        }

        public Task SetRoomControlModeAsync(
            int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
            DateTimeOffset? until, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetWeekDayProfileAsync(
            int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            State = CubeConnectionState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }
    }

    private sealed record MetadataCall(IReadOnlyList<string> RoomNames, IReadOnlyList<string> DeviceNames);

    private sealed class FakeAdmin : ICubeAdminGateway
    {
        public List<string> Calls { get; } = new();
        public List<RoomConfigurationValues> Configurations { get; } = new();
        public List<ValveFunctionValues> ValveFunctions { get; } = new();
        public List<(RadioAddress Device, bool ShowActual)> Displays { get; } = new();
        public Queue<FoundDevice> Found { get; } = new();
        public List<MetadataCall> Metadata { get; } = new();
        public List<PushButtonConfiguration> PushButtons { get; } = new();
        public List<(TimeZoneConfiguration Configuration, DateTimeOffset? Time)> TimeZones { get; } = new();
        public List<string[]> Ntp { get; } = new();
        public List<(string Kind, int Destination, int Minutes)> WakeUps { get; } = new();
        public bool FailMetadata { get; set; }

        public event EventHandler<AirtimeStatus>? AirtimeChanged;

        public void RaiseAirtime(AirtimeStatus status) => AirtimeChanged?.Invoke(this, status);

        public Task SetRoomConfigurationAsync(
            int roomId, RadioAddress master, RoomConfigurationValues values, CancellationToken cancellationToken = default)
        {
            Calls.Add($"config:{roomId}");
            Configurations.Add(values);
            return Task.CompletedTask;
        }

        public Task SetValveFunctionsAsync(
            int roomId, RadioAddress master, ValveFunctionValues values, CancellationToken cancellationToken = default)
        {
            Calls.Add($"valve:{roomId}");
            ValveFunctions.Add(values);
            return Task.CompletedTask;
        }

        public Task SetDisplayModeAsync(
            RadioAddress wallThermostat, bool showActualTemperature, CancellationToken cancellationToken = default)
        {
            Calls.Add($"display:{wallThermostat}");
            Displays.Add((wallThermostat, showActualTemperature));
            return Task.CompletedTask;
        }

        public Task<FoundDevice?> SearchNewDeviceAsync(TimeSpan window, CancellationToken cancellationToken = default)
        {
            Calls.Add("search");
            return Task.FromResult(Found.TryDequeue(out var found) ? found : null);
        }

        public Task EndInclusionAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("endInclusion");
            return Task.CompletedTask;
        }

        public Task SetGroupAddressAsync(RadioAddress device, int groupId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"group:{device}:{groupId}");
            return Task.CompletedTask;
        }

        public Task RemoveGroupAddressAsync(RadioAddress device, int roomId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"removeGroup:{device}:{roomId}");
            return Task.CompletedTask;
        }

        public Task SetLinkPartnerAsync(
            RadioAddress device, RadioAddress partner, int partnerNumericType, bool add,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"link:{device}->{partner}:{(add ? "add" : "remove")}");
            return Task.CompletedTask;
        }

        public Task SetLinkPartnerInRoomAsync(
            RadioAddress master, int roomId, RadioAddress partner, int partnerNumericType, bool add,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"linkroom:{roomId}:{master}->{partner}:{(add ? "add" : "remove")}");
            return Task.CompletedTask;
        }

        public Task DeleteDevicesAsync(
            IReadOnlyList<RadioAddress> devices, bool factoryReset, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{string.Join(',', devices)}:{(factoryReset ? "force" : "soft")}");
            return Task.CompletedTask;
        }

        public Task WriteMetadataAsync(Cube cube, CancellationToken cancellationToken = default)
        {
            if (FailMetadata)
            {
                throw new InvalidOperationException("Simulated metadata failure.");
            }

            Calls.Add("metadata");
            Metadata.Add(new MetadataCall(
                cube.Rooms.Select(room => room.Name).ToArray(),
                cube.Devices.Select(device => device.Name).ToArray()));
            return Task.CompletedTask;
        }

        public Task<PushButtonConfiguration> ReadPushButtonAsync(RadioAddress cubeAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(new PushButtonConfiguration(
                new PushButtonKeyConfiguration(PushButtonKeyMode.Auto, null),
                new PushButtonKeyConfiguration(PushButtonKeyMode.Auto, null)));

        public Task SetPushButtonAsync(
            RadioAddress cubeAddress, PushButtonConfiguration configuration, CancellationToken cancellationToken = default)
        {
            PushButtons.Add(configuration);
            return Task.CompletedTask;
        }

        public Task<TimeZoneConfiguration> ReadTimeZoneAsync(RadioAddress cubeAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);

        public Task SetTimeZoneAsync(
            RadioAddress cubeAddress, TimeZoneConfiguration configuration, DateTimeOffset? setTimeUtc,
            CancellationToken cancellationToken = default)
        {
            TimeZones.Add((configuration, setTimeUtc));
            return Task.CompletedTask;
        }

        public Task<string> QueryNtpServersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("pool.ntp.org");

        public Task SetNtpServersAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
        {
            Ntp.Add(servers.ToArray());
            return Task.CompletedTask;
        }

        public Task WakeUpRoomAsync(int roomId, int durationMinutes = 30, CancellationToken cancellationToken = default)
        {
            WakeUps.Add(("room", roomId, durationMinutes));
            return Task.CompletedTask;
        }

        public Task WakeUpDeviceAsync(
            RadioAddress device, int durationMinutes = 30, CancellationToken cancellationToken = default)
        {
            WakeUps.Add(("device", device.Value, durationMinutes));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNetwork : ICubeNetworkConfigurator
    {
        public List<(string Serial, string Ip, CubeNetworkConfiguration Configuration)> Written { get; } = new();
        public Func<CancellationToken, Task>? WriteHook { get; set; }

        public Task<CubeNetworkConfiguration> ReadAsync(string serialNumber, string ipAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(new CubeNetworkConfiguration("1.2.3.4", "255.255.255.0", "1.2.3.1", "1.2.3.1", "8.8.8.8", true));

        public async Task WriteAsync(
            string serialNumber, string ipAddress, CubeNetworkConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (WriteHook is not null)
            {
                await WriteHook(cancellationToken);
            }

            Written.Add((serialNumber, ipAddress, configuration));
        }
    }

    private sealed class FakeStore : ICubeStateStore
    {
        public Dictionary<string, string> Settings { get; } = new();
        public CubeSnapshot? Snapshot { get; set; }
        public List<string> Audit { get; } = new();
        public DateTimeOffset? DeletedBefore { get; private set; }

        public Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings.TryGetValue(key, out var value) ? value : null);

        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Settings[key] = value;
            return Task.CompletedTask;
        }

        public Task AppendAuditAsync(
            string message, AuditSource source = AuditSource.User, CancellationToken cancellationToken = default)
        {
            Audit.Add($"[{source.ToString().ToLowerInvariant()}] {message}");
            return Task.CompletedTask;
        }

        public Task<int> DeleteAuditOlderThanAsync(
            DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            DeletedBefore = cutoff;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Audit.ToArray());
    }
}

public class ErrorCatalogTests
{
    [Fact]
    public void Every_documented_code_is_in_the_catalog()
    {
        ErrorCatalog.All.Should().HaveCount(52);
        ErrorCatalog.All.Select(entry => entry.Code).Should().OnlyHaveUniqueItems();
        ErrorCatalog.Find("MAX1015")!.Symbol.Should().Be("DeviceNameAlreadyExists");
        ErrorCatalog.Find("max10038")!.Symbol.Should().Be("RemoteServiceUnavailable");
        ErrorCatalog.Find("MAX9999").Should().BeNull();
    }

    [Fact]
    public void Messages_are_explained_in_both_languages_and_unknown_codes_stay_unexplained()
    {
        ErrorCatalog.Describe("MAX1030: Device name exceeds 32 characters.")
            .Should().Be("MAX1030: Der Name ist zu lang (höchstens 32 Zeichen)");
        ErrorCatalog.Describe("MAX1030: x", german: false).Should().Contain("too long");
        ErrorCatalog.Describe("MAX9999: ???").Should().BeNull();
        ErrorCatalog.Describe("kein Code").Should().BeNull();
        ErrorCatalog.Describe(null).Should().BeNull();
    }

    [Theory]
    [InlineData(272, false)]
    [InlineData(273, true)]
    [InlineData(275, true)]
    [InlineData(276, false)]
    public void Firmware_window_is_273_to_275(int firmware, bool supported)
    {
        FirmwareSupport.IsSupported(firmware).Should().Be(supported);
    }
}
