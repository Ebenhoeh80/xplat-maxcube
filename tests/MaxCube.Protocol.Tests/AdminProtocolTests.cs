using System.Text;
using FluentAssertions;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Tests;

/// <summary>Gremium Runde 5: Konfiguration, Zuordnung, Metadaten-Encoder und Cube-Codecs.</summary>
public class AdminProtocolTests
{
    private static readonly RadioAddress Master = new(0x1D2A3C);
    private static readonly RadioAddress Device = new(0x0A0B0C);

    private static string Wire(string hex) => "s:" + Convert.ToBase64String(Convert.FromHexString(hex));

    [Fact]
    public void Valve_functions_mirror_the_read_path_bit_layout()
    {
        var command = new SetRoomValveFunctionsCommand(
            Master, 1, boostMinutes: 15, boostValveAngle: 80, DayOfWeek.Saturday, 12, maxValveRaw: 255, valveOffsetRaw: 0);

        // 0x70 = Boost-Code 3 (15 min) in Bit 5..7 + Ventil 80/5 = 16; 0x0C = Samstag (0) + 12 Uhr;
        // 0xFF/0x00 = durchgereichte Rohwerte.
        command.WireLine.Should().Be(Wire("0004120000001d2a3c01700cff00"));
    }

    [Fact]
    public void Decalcification_weekday_uses_the_java_renderer_value()
    {
        var command = new SetRoomValveFunctionsCommand(Master, 1, 60, 100, DayOfWeek.Monday, 6, 255, 0);

        var bytes = Convert.FromBase64String(command.WireLine[2..]);
        bytes[10].Should().Be((byte)((7 << 5) | 20)); // 60 min = Code 7, 100/5 = 20
        bytes[11].Should().Be((byte)((2 << 5) | 6));  // (Montag 1 + 1) % 7 = 2
    }

    [Theory]
    [InlineData(0, DayOfWeek.Saturday)]
    [InlineData(1, DayOfWeek.Sunday)]
    [InlineData(2, DayOfWeek.Monday)]
    [InlineData(3, DayOfWeek.Tuesday)]
    [InlineData(4, DayOfWeek.Wednesday)]
    [InlineData(5, DayOfWeek.Thursday)]
    [InlineData(6, DayOfWeek.Friday)]
    public void Decalcification_day_code_is_the_inverse_of_the_renderer(int code, DayOfWeek expected)
    {
        CubeStateAssembler.DecalcificationDayFromCode(code).Should().Be(expected);
        // Rückweg: Writer und Reader müssen sich gegenseitig aufheben.
        (((int)expected) + 1).Should().BeInRange(1, 8);
        ((((int)expected) + 1) % 7).Should().Be(code);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(45)]
    [InlineData(-5)]
    public void Boost_duration_outside_the_code_table_is_rejected(int minutes)
    {
        var act = () => new SetRoomValveFunctionsCommand(Master, 1, minutes, 80, DayOfWeek.Saturday, 12, 255, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Valve_angle_must_be_a_multiple_of_five()
    {
        var act = () => new SetRoomValveFunctionsCommand(Master, 1, 15, 82, DayOfWeek.Saturday, 12, 255, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Display_mode_matches_the_live_confirmed_wall_thermostat_message()
    {
        // ha-maxcube display.py: 0000 82 000000 <RF> 00 <04|00>, 11 Byte.
        new SetDisplayModeCommand(Device, showActualTemperature: true).WireLine
            .Should().Be(Wire("0000820000000a0b0c0004"));
        new SetDisplayModeCommand(Device, showActualTemperature: false).WireLine
            .Should().Be(Wire("0000820000000a0b0c0000"));
    }

    [Fact]
    public void Group_and_link_telegrams_carry_their_payload_after_the_device_header()
    {
        new SetGroupAddressCommand(Device, 3).WireLine.Should().Be(Wire("0000220000000a0b0c0003"));
        new RemoveGroupAddressCommand(Device, 3).WireLine.Should().Be(Wire("0000230000000a0b0c0003"));
        new LinkPartnerCommand(Device, Master, 3, add: true).WireLine
            .Should().Be(Wire("0000200000000a0b0c001d2a3c03"));
        new LinkPartnerCommand(Device, Master, 3, add: false).WireLine
            .Should().Be(Wire("0000210000000a0b0c001d2a3c03"));
    }

    private static byte[] SampleMetadata(bool serialPaddedWithNul = false)
    {
        var bytes = new List<byte> { MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion, 1, 1, 6 };
        bytes.AddRange(Encoding.UTF8.GetBytes("Wohnen"));
        bytes.AddRange([0x1D, 0x2A, 0x3C]);
        bytes.Add(1);
        bytes.Add(1);
        bytes.AddRange([0x0A, 0x0B, 0x0C]);
        var serial = Encoding.ASCII.GetBytes("JEQ00012".PadRight(10, serialPaddedWithNul ? '\0' : ' '));
        bytes.AddRange(serial);
        bytes.Add(10);
        bytes.AddRange(Encoding.UTF8.GetBytes("Thermostat"));
        bytes.Add(1);
        bytes.Add(1);
        return bytes.ToArray();
    }

    [Fact]
    public void Encoder_reproduces_the_decoded_bytes_exactly()
    {
        var original = SampleMetadata();

        var encoded = MetadataEncoder.Encode(MetadataDecoder.Decode(original));

        encoded.Should().Equal(original);
        FluentActions.Invoking(() => MetadataEncoder.VerifyRoundTrip(Convert.ToBase64String(original)))
            .Should().NotThrow();
    }

    [Fact]
    public void Safety_check_blocks_a_payload_the_encoder_cannot_reproduce()
    {
        // Ungültiges UTF-8 im Raumnamen: Decoder und Encoder würden daraus andere Bytes machen.
        var bytes = SampleMetadata();
        bytes[5] = 0xFF;
        var original = Convert.ToBase64String(bytes);

        FluentActions.Invoking(() => MetadataEncoder.VerifyRoundTrip(original))
            .Should().Throw<MalformedMetadataException>().WithMessage("*Nothing was sent*");
    }

    [Fact]
    public void Nul_padded_serial_numbers_are_preserved_losslessly()
    {
        var original = SampleMetadata(serialPaddedWithNul: true);

        MetadataEncoder.Encode(MetadataDecoder.Decode(original)).Should().Equal(original);
    }

    [Fact]
    public void Renamed_room_survives_the_round_trip()
    {
        var snapshot = MetadataDecoder.Decode(SampleMetadata());
        var renamed = snapshot with
        {
            Rooms = [snapshot.Rooms[0] with { Name = "Büro Ü" }],
            DaylightSaving = false,
        };

        var decoded = MetadataDecoder.Decode(MetadataEncoder.Encode(renamed));

        decoded.Rooms[0].Name.Should().Be("Büro Ü");
        decoded.DaylightSaving.Should().BeFalse();
        decoded.Devices.Should().ContainSingle().Which.Name.Should().Be("Thermostat");
    }

    [Fact]
    public void Blocks_are_split_at_the_protocol_size_and_reassemble_losslessly()
    {
        var text = new string('A', SendMetadataBlockCommand.BlockSize * 2 + 10);

        var blocks = MetadataEncoder.SplitIntoBlocks(text);

        blocks.Should().HaveCount(3);
        blocks[0].Length.Should().Be(SendMetadataBlockCommand.BlockSize);
        blocks[2].Length.Should().Be(10);
        Encoding.ASCII.GetString(blocks.SelectMany(block => block).ToArray()).Should().Be(text);
    }

    [Fact]
    public void More_than_sixteen_blocks_are_refused()
    {
        var text = new string('A', SendMetadataBlockCommand.BlockSize * 16 + 1);

        FluentActions.Invoking(() => MetadataEncoder.SplitIntoBlocks(text))
            .Should().Throw<MalformedMetadataException>();
    }

    [Fact]
    public void New_device_is_decoded_and_the_plus_model_is_normalised()
    {
        var payload = new List<byte> { 2, 0x0A, 0x0B, 0x0C };
        payload.AddRange(Encoding.ASCII.GetBytes("JEQ0007777"));

        var found = NewDeviceDecoder.Decode("N:" + Convert.ToBase64String(payload.ToArray()));

        found.Should().NotBeNull();
        found!.DeviceType.Should().Be(DeviceType.HeatingThermostat);
        found.NumericDeviceType.Should().Be(2);
        found.RadioAddress.Should().Be(Device);
        found.SerialNumber.Should().Be("JEQ0007777");
    }

    [Theory]
    [InlineData("")]
    [InlineData("N:")]
    [InlineData("N:%%%")]
    [InlineData("N:AQID")]
    public void Empty_or_broken_new_device_lines_end_the_search(string line)
    {
        NewDeviceDecoder.Decode(line).Should().BeNull();
    }

    private static byte[] CubeConfiguration()
    {
        var bytes = new byte[CubeConfigurationCodec.MinimumLength + 4];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        bytes[CubeConfigurationCodec.PushButtonOffsetUpper + CubeConfigurationCodec.PushButtonKeyByte] = (1 << 6) | 2; // Comfort
        bytes[CubeConfigurationCodec.PushButtonOffsetLower + CubeConfigurationCodec.PushButtonKeyByte] = 0;            // Auto
        return bytes;
    }

    [Fact]
    public void Push_button_is_read_from_the_two_templates()
    {
        var configuration = CubeConfigurationCodec.ReadPushButton(CubeConfiguration());

        configuration.Upper.Mode.Should().Be(PushButtonKeyMode.Comfort);
        configuration.Lower.Mode.Should().Be(PushButtonKeyMode.Auto);
    }

    [Fact]
    public void Push_button_payload_changes_only_the_two_key_bytes()
    {
        var current = CubeConfiguration();
        var desired = new PushButtonConfiguration(
            new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, new Temperature(22.0)),
            new PushButtonKeyConfiguration(PushButtonKeyMode.Eco, null));

        var payload = CubeConfigurationCodec.BuildPushButtonPayload(current, desired);

        payload.Should().HaveCount(66);
        payload[11].Should().Be((byte)((1 << 6) | 44)); // Manuell 22,0 °C
        payload[33 + 11].Should().Be((byte)((1 << 6) | 1)); // Eco
        for (var index = 0; index < 33; index++)
        {
            if (index == 11)
            {
                continue;
            }

            payload[index].Should().Be(current[CubeConfigurationCodec.PushButtonOffsetUpper + index]);
            payload[33 + index].Should().Be(current[CubeConfigurationCodec.PushButtonOffsetLower + index]);
        }
    }

    [Theory]
    [InlineData(PushButtonKeyMode.Auto, null)]
    [InlineData(PushButtonKeyMode.AutoWithTemperature, 19.5)]
    [InlineData(PushButtonKeyMode.Manual, 25.0)]
    [InlineData(PushButtonKeyMode.Eco, null)]
    [InlineData(PushButtonKeyMode.Comfort, null)]
    public void Key_modes_round_trip(PushButtonKeyMode mode, double? temperature)
    {
        var key = new PushButtonKeyConfiguration(mode, temperature is null ? null : new Temperature(temperature.Value));

        var decoded = CubeConfigurationCodec.DecodeKey(CubeConfigurationCodec.EncodeKey(key));

        decoded.Mode.Should().Be(mode);
        decoded.Temperature?.Value.Should().Be(temperature);
    }

    [Fact]
    public void Key_modes_that_need_a_temperature_refuse_to_encode_without_one()
    {
        var act = () => CubeConfigurationCodec.EncodeKey(
            new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, null));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Time_zone_round_trips_and_detects_daylight_saving()
    {
        var preset = TimeZonePresets.All["Mitteleuropa (CET/CEST)"];

        var payload = CubeConfigurationCodec.BuildTimeZonePayload(preset);
        var configuration = new byte[CubeConfigurationCodec.MinimumLength];
        Array.Copy(payload, 0, configuration, CubeConfigurationCodec.TimeZoneOffsetWinter, payload.Length);
        var read = CubeConfigurationCodec.ReadTimeZone(configuration);

        payload.Should().HaveCount(24);
        read.Should().Be(preset);
        read.UsesDaylightSaving.Should().BeTrue();
        TimeZonePresets.All["Mitteleuropa ohne Sommerzeit"].UsesDaylightSaving.Should().BeFalse();
        CubeConfigurationCodec.BuildTimeZonePayload(read).Should().Equal(payload);
    }

    [Fact]
    public void Time_zone_offset_is_encoded_big_endian()
    {
        var payload = CubeConfigurationCodec.BuildTimeZonePayload(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);

        // Winter: Name(5) Monat 10, Sonntag 0, 03 Uhr, 3600 s = 0x00000E10
        payload.Skip(5).Take(7).Should().Equal(10, 0, 3, 0x00, 0x00, 0x0E, 0x10);
    }

    [Fact]
    public void Short_cube_configuration_is_refused_instead_of_guessed()
    {
        FluentActions.Invoking(() => CubeConfigurationCodec.ReadTimeZone(new byte[40]))
            .Should().Throw<CubeCommunicationFailedException>();
        FluentActions.Invoking(() => CubeConfigurationCodec.DecodeResponse("1d2a3c," + Convert.ToBase64String(new byte[10])))
            .Should().Throw<CubeCommunicationFailedException>();
    }

    [Fact]
    public void Cube_configuration_response_is_decoded_from_the_C_line_body()
    {
        var body = "1d2a3c," + Convert.ToBase64String(CubeConfiguration());

        CubeConfigurationCodec.DecodeResponse(body).Should().Equal(CubeConfiguration());
    }
}
