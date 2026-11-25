using System.Text;
using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Protocol.Handshake;
using MaxCube.Protocol.State;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Tests;

public class CubeStateAssemblerTests
{
    private static CubeHello Hello() => new(
        "JEQ0001234", 0x1D2A3C, 275, null, null, null, null, null, null, "JEQ0001234,1D2A3C,0113,00,00");

    private static byte[] Metadata()
    {
        var bytes = new List<byte> { MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion, 1, 1 };
        bytes.Add(6);
        bytes.AddRange(Encoding.UTF8.GetBytes("Wohnen"));
        bytes.AddRange([0x1D, 0x2A, 0x3C]); // Master
        bytes.Add(1);                       // deviceCount
        bytes.Add(1);                       // Gerätetyp HeatingThermostat
        bytes.AddRange([0x0A, 0x0B, 0x0C]);
        bytes.AddRange(Encoding.UTF8.GetBytes("JEQ0001234".PadRight(10)));
        bytes.Add(10);
        bytes.AddRange(Encoding.UTF8.GetBytes("Thermostat"));
        bytes.Add(1);                       // Raum 1
        bytes.Add(1);                       // DaylightSaving
        return bytes.ToArray();
    }

    private static byte[] Configuration()
    {
        var bytes = new List<byte>
        {
            0x00, 0x0A, 0x0B, 0x0C, 0x01, 0x01, 0x52, 0x00, // Header + Typ + Gruppe + Firmware + Test
        };
        bytes.AddRange(Encoding.UTF8.GetBytes("JEQ0001234".PadRight(10)));
        bytes.AddRange([
            // 21,5 / 16,5 / 30,5 / 4,5 / Offset 0 / 12,0 / Fenster 15 min /
            // Boost 15 min + Ventil 80 % (Byte 7 Bit 5..7 = 3, Bit 0..4 = 16) / Sa 12
            43, 33, 61, 9, 7, 24, 3, 0x70, 12,
        ]);
        bytes.AddRange(new byte[8]); // Wochenprofil in diesem Test nicht untersucht
        return bytes.ToArray();
    }

    private static byte[] ConfigurationWithWeekProfile()
    {
        // Vollständiger C:-Block: 18 Byte Header, 9 Werte, 2 Reserve-Byte und
        // 7x13 Einträge à 2 Byte ab config[11] (WeekProfileDecoder.ProfileOffset).
        var bytes = Configuration().Take(27).ToList();
        bytes.AddRange([0, 0]);
        for (var day = 0; day < 7; day++)
        {
            bytes.AddRange([0x54, 0x48]); // 06:00, 21,0 °C
            bytes.AddRange([0x54, 0x00]); // 00:00 beendet den Tag, 21,0 °C
            bytes.AddRange(new byte[22]); // 11 weitere, nicht konsumierte Einträge
        }

        return bytes.ToArray();
    }

    private static byte[] DeviceList()
    {
        // 13 Byte pro Block: 1 Länge, 3 Adresse, 1 FrameType, 1 StateInfo, 8 Statusbytes.
        // state[0]=Modus 1 (Permanently), state[1]=Ventil, state[2]=42 (21,0 °C Sollwert).
        return [
            13, 0x0A, 0x0B, 0x0C, 0x00, 0x12, 0x01, 0x00, 42, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];
    }

    [Fact]
    public async Task Assembles_hello_metadata_configuration_and_device_list()
    {
        var transport = new ScriptedCubeTransport(
            "C:0a0b0c," + Convert.ToBase64String(Configuration()),
            // M:-Payload ist bereits die Base64-Textdarstellung der Metadaten (MetaDataSerializer.java:40-43).
            "M:00,01," + Convert.ToBase64String(Metadata()),
            "L:" + Convert.ToBase64String(DeviceList()));
        var raw = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello());

        var cube = CubeStateAssembler.Assemble(Hello(), raw);

        cube.SerialNumber.ToString().Should().Be("JEQ0001234");
        cube.FirmwareVersion.Should().Be(275);
        cube.DaylightSaving.Should().BeTrue();
        cube.Rooms.Should().ContainSingle();
        var room = cube.Rooms[0];
        room.Name.Should().Be("Wohnen");
        room.MasterRadioAddress.ToString().Should().Be("1D2A3C");
        room.SetPointTemperature.Value.Should().Be(21.0);
        room.ControlMode.Should().Be(RoomControlMode.Permanently);
        room.ComfortTemperature.Value.Should().Be(21.5);
        room.EcoTemperature.Value.Should().Be(16.5);
        room.MaximumTemperature.Value.Should().Be(30.5);
        room.MinimumTemperature.Value.Should().Be(4.5);
        room.WindowOpenTemperature.Value.Should().Be(12.0);
        room.WindowOpenDurationMinutes.Should().Be(15);
        room.BoostDurationMinutes.Should().Be(15);
        room.BoostValveAngle.Should().Be(80);
        room.DecalcificationDay.Should().Be(DayOfWeek.Saturday);
        room.DecalcificationHour.Should().Be(12);
        cube.Devices.Should().ContainSingle();
        cube.Devices[0].SerialNumber.ToString().Should().Be("JEQ0001234");
        cube.Devices[0].StateInfo.Should().Be(DeviceStateInfo.Valid);
        cube.Devices[0].RadioState.Should().Be(RadioState.Ok);
    }

    [Fact]
    public async Task Full_configuration_loads_the_week_profile_into_the_room()
    {
        // Gremium Runde 2: Ende-zu-Ende C: -> Assembler -> Room.WeekProfile -> Editor-Sperre.
        var transport = new ScriptedCubeTransport(
            "C:0a0b0c," + Convert.ToBase64String(ConfigurationWithWeekProfile()),
            "M:00,01," + Convert.ToBase64String(Metadata()),
            "L:" + Convert.ToBase64String(DeviceList()));
        var raw = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello());

        var cube = CubeStateAssembler.Assemble(Hello(), raw);

        var room = cube.Rooms.Should().ContainSingle().Subject;
        room.HasWeekProfile.Should().BeTrue();
        room.WeekProfile!.Days.Should().HaveCount(7);
        var saturday = room.WeekProfile.GetDay(DayOfWeek.Saturday)!;
        saturday.Points.Should().HaveCount(2);
        saturday.Points[0].Until.Should().Be(new TimeOnly(6, 0));
        saturday.Points[0].Temperature.Value.Should().Be(21.0);
        saturday.Points[1].Until.Should().Be(TimeOnly.MinValue);
    }

    [Fact]
    public async Task Empty_state_yields_an_empty_but_valid_cube()
    {
        var transport = new ScriptedCubeTransport("L:" + Convert.ToBase64String(Array.Empty<byte>()));
        var raw = await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello());

        var cube = CubeStateAssembler.Assemble(Hello(), raw);

        cube.Rooms.Should().BeEmpty();
        cube.Devices.Should().BeEmpty();
        cube.House.Devices.Should().BeEmpty();
    }
}
