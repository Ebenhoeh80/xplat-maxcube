using System.Text;
using FluentAssertions;
using MaxCube.Protocol.Handshake;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Protocol.Tests;

/// <summary>
/// Sonnet, zweiter Gesamtdurchgang H-C/M-E: ein einzelnes fehlerhaftes Gerät darf den
/// Connect nicht abbrechen, ungültige Zustände liefern keine Parameter, und die Raumwerte
/// stammen vom Master-Thermostat (DeviceStateSerializer.java:64-98).
/// </summary>
public class CubeStateAssemblerRobustnessTests
{
    private static CubeHello Hello() => new(
        "JEQ0001234", 0x1D2A3C, 275, null, null, null, null, null, null, "JEQ0001234,1D2A3C,0113,00,00");

    private static byte[] Metadata(int master, params (int Type, int Address, string Name)[] devices)
    {
        var bytes = new List<byte> { MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion, 1, 1 };
        bytes.Add(6);
        bytes.AddRange(Encoding.UTF8.GetBytes("Wohnen"));
        bytes.AddRange([(byte)(master >> 16), (byte)(master >> 8), (byte)master]);
        bytes.Add((byte)devices.Length);
        foreach (var (type, address, name) in devices)
        {
            bytes.Add((byte)type);
            bytes.Add((byte)(address >> 16));
            bytes.Add((byte)(address >> 8));
            bytes.Add((byte)address);
            bytes.AddRange(Encoding.UTF8.GetBytes("JEQ0001234".PadRight(10)));
            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.UTF8.GetBytes(name));
            bytes.Add(1); // Raum 1
        }

        bytes.Add(1); // DaylightSaving
        return bytes.ToArray();
    }

    private static byte[] Block(int address, byte info, params byte[] state)
    {
        var bytes = new List<byte> { (byte)(5 + state.Length) };
        bytes.Add((byte)(address >> 16));
        bytes.Add((byte)(address >> 8));
        bytes.Add((byte)address);
        bytes.Add(0); // erroneousFrameType
        bytes.Add(info);
        bytes.AddRange(state);
        return bytes.ToArray();
    }

    private static async Task<RawCubeState> Receive(byte[] metadata, params byte[][] blocks)
    {
        var deviceList = blocks.SelectMany(block => block).ToArray();
        var transport = new ScriptedCubeTransport(
            "M:00,01," + Convert.ToBase64String(metadata),
            "L:" + Convert.ToBase64String(deviceList));
        return await new StateReceiver(new CubeConnectionOptions()).ReceiveAsync(transport, Hello());
    }

    [Fact]
    public async Task A_single_broken_device_does_not_abort_the_connect()
    {
        var metadata = Metadata(
            0x1D2A3C,
            (1, 0x0A0B0C, "Thermostat"),
            (3, 0x0A0B0D, "Wand"));
        var raw = await Receive(
            metadata,
            // info 0x00: nicht gültig → Sollwert 30,5 darf nicht übernommen werden.
            Block(0x0A0B0C, 0x00, 2, 0, 61, 0, 0, 0, 0, 0),
            // Gültiger Wandthermostat: 200 Zehntel °C → 20,0 °C Ist.
            Block(0x0A0B0D, 0x12, 0, 0, 42, 0, 0, 0, 200, 0),
            // Gerät ohne Metadaten wird verworfen.
            Block(0xFFFFFF, 0x12, 2, 0, 63, 0, 0, 0, 0, 0));

        var cube = CubeStateAssembler.Assemble(Hello(), raw);

        var room = cube.Rooms.Should().ContainSingle().Subject;
        room.SetPointTemperature.Value.Should().Be(21.0);
        room.HasActualTemperature.Should().BeTrue();
        room.ActualTemperature.Value.Should().Be(20.0);
        cube.Devices.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_implausible_wall_temperature_is_not_reported_as_a_measurement()
    {
        // Sonnet N4: 456 Zehntel °C dürfen nicht als erfundene 40,0 °C erscheinen.
        // Pro Raum ist nur ein Wandthermostat erlaubt, daher ein eigener Testfall.
        var metadata = Metadata(0x1D2A3C, (3, 0x0A0B0D, "Wand"));
        var raw = await Receive(
            metadata,
            Block(0x0A0B0D, 0x12, 0, 0, 0x80 | 42, 0, 0, 0, 200, 0));

        var cube = CubeStateAssembler.Assemble(Hello(), raw);
        var room = cube.Rooms.Should().ContainSingle().Subject;

        room.HasActualTemperature.Should().BeFalse();
        room.ActualTemperature.Value.Should().Be(0.0);
    }

    [Fact]
    public async Task The_master_thermostat_wins_over_a_secondary_thermostat()
    {
        // Der Master ist das zweite Gerät im L:-Block. Ohne Master-Regel würde das
        // erste Gerät gewinnen und der Test 30,5 °C liefern (Sonnet N2).
        var metadata = Metadata(0x0A0B0E, (1, 0x0A0B0C, "Thermostat"), (1, 0x0A0B0E, "Master"));
        var raw = await Receive(
            metadata,
            Block(0x0A0B0C, 0x12, 1, 0, 61, 0, 0, 0, 0, 0),  // Zweitgerät: 30,5 °C
            Block(0x0A0B0E, 0x12, 1, 0, 42, 0, 0, 0, 0, 0)); // Master: 21,0 °C

        var cube = CubeStateAssembler.Assemble(Hello(), raw);

        cube.Rooms.Should().ContainSingle()
            .Which.SetPointTemperature.Value.Should().Be(21.0);
    }
}
