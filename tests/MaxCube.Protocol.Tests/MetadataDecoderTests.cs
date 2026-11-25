using System.Text;
using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class MetadataDecoderTests
{
    private static byte[] BuildPayload(
        bool withVersion2Fields,
        string roomName = "Wohnen",
        byte daylight = 1,
        bool includeDaylight = true)
    {
        var bytes = new List<byte>();
        if (withVersion2Fields)
        {
            bytes.Add(MetadataDecoder.MagicByte);
            bytes.Add(MetadataDecoder.ActualVersion);
        }

        // Ein Raum: Id 1, Name "Wohnen", (V2) Master 0x1D2A3C.
        bytes.Add(1);
        bytes.Add(1);
        AppendUtf8(bytes, roomName);
        if (withVersion2Fields)
        {
            bytes.AddRange([0x1D, 0x2A, 0x3C]);
        }

        // Ein Gerät: Typ 1, Adresse 0x0A0B0C, Serie, Name "Thermostat", Raum 1.
        bytes.Add(1);
        bytes.Add(1);
        bytes.AddRange([0x0A, 0x0B, 0x0C]);
        bytes.AddRange(Encoding.UTF8.GetBytes("JEQ0001234".PadRight(10)));
        AppendUtf8(bytes, "Thermostat");
        bytes.Add(1);

        if (includeDaylight)
        {
            bytes.Add(daylight);
        }

        return bytes.ToArray();
    }

    private static void AppendUtf8(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        bytes.Add((byte)encoded.Length);
        bytes.AddRange(encoded);
    }

    [Fact]
    public void Decodes_version_2_payload_from_base64()
    {
        var base64 = Convert.ToBase64String(BuildPayload(withVersion2Fields: true));

        var snapshot = MetadataDecoder.Decode(base64);

        snapshot.Version.Should().Be(2);
        snapshot.DaylightSaving.Should().BeTrue();
        snapshot.Rooms.Should().ContainSingle();
        snapshot.Rooms[0].Id.Should().Be(1);
        snapshot.Rooms[0].Name.Should().Be("Wohnen");
        snapshot.Rooms[0].MasterRadioAddress.ToString().Should().Be("1D2A3C");
        snapshot.Devices.Should().ContainSingle();
        snapshot.Devices[0].DeviceType.Should().Be(DeviceType.HeatingThermostat);
        snapshot.Devices[0].NumericDeviceType.Should().Be(1);
        snapshot.Devices[0].RadioAddress.ToString().Should().Be("0A0B0C");
        snapshot.Devices[0].SerialNumber.Should().Be("JEQ0001234");
        snapshot.Devices[0].Name.Should().Be("Thermostat");
        snapshot.Devices[0].RoomId.Should().Be(1);
    }

    [Fact]
    public void Decodes_version_1_payload_without_master_and_daylight_saving()
    {
        var snapshot = MetadataDecoder.Decode(BuildPayload(withVersion2Fields: false));

        snapshot.Version.Should().Be(1);
        snapshot.Rooms[0].MasterRadioAddress.Value.Should().Be(0);
        // BooleanSerializer hat bei Version 1 den Default true (MetaDataSerializer.java:42).
        snapshot.DaylightSaving.Should().BeTrue();
        snapshot.Devices[0].RadioAddress.ToString().Should().Be("0A0B0C");
    }

    [Fact]
    public void Device_type_plus_is_normalized_to_heating_thermostat()
    {
        var payload = BuildPayload(withVersion2Fields: true);
        // V2-Layout: Magic+Version(2), RoomCount+Id(2), Name(Länge+6=7), Master(3), DeviceCount(1) → Typbyte bei 15.
        payload[15] = 2;

        var snapshot = MetadataDecoder.Decode(payload);

        snapshot.Devices[0].NumericDeviceType.Should().Be(2);
        snapshot.Devices[0].DeviceType.Should().Be(DeviceType.HeatingThermostat);
    }

    [Fact]
    public void Truncated_field_inside_an_entry_is_reported()
    {
        var payload = BuildPayload(withVersion2Fields: true);

        // 25 Bytes enden mitten in der 10-Byte-Seriennummer (Offset 19..28).
        var act = () => MetadataDecoder.Decode(payload.AsSpan(0, 25));

        act.Should().Throw<MalformedMetadataException>()
            .WithMessage("*device serial number*");
    }

    [Fact]
    public void Empty_payload_matches_the_java_empty_state()
    {
        var fromBytes = MetadataDecoder.Decode(ReadOnlySpan<byte>.Empty);
        var fromBase64 = MetadataDecoder.Decode(string.Empty);

        foreach (var snapshot in new[] { fromBytes, fromBase64 })
        {
            snapshot.Version.Should().Be(1);
            snapshot.Rooms.Should().BeEmpty();
            snapshot.Devices.Should().BeEmpty();
            // Java: BooleanSerializer-Default true bei Version 1 (MetaDataSerializer.java:42).
            snapshot.DaylightSaving.Should().BeTrue();
        }
    }

    [Fact]
    public void Header_only_v2_has_no_entries_and_daylight_false()
    {
        var snapshot = MetadataDecoder.Decode([MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion]);

        snapshot.Version.Should().Be(2);
        snapshot.Rooms.Should().BeEmpty();
        snapshot.Devices.Should().BeEmpty();
        // Java: stream.read() = -1 → Boolean.valueOf(-1 > 0) = false (BooleanSerializer.java:23-24).
        snapshot.DaylightSaving.Should().BeFalse();
    }

    [Fact]
    public void Missing_daylight_byte_in_v2_is_false()
    {
        var snapshot = MetadataDecoder.Decode(
            BuildPayload(withVersion2Fields: true, includeDaylight: false));

        snapshot.DaylightSaving.Should().BeFalse();
        snapshot.Rooms.Should().ContainSingle();
        snapshot.Devices.Should().ContainSingle();
    }

    [Fact]
    public void Multibyte_utf8_names_are_counted_in_bytes()
    {
        var snapshot = MetadataDecoder.Decode(BuildPayload(true, roomName: "Küchenraum"));

        snapshot.Rooms[0].Name.Should().Be("Küchenraum");
        snapshot.Devices[0].Name.Should().Be("Thermostat");
    }

    [Fact]
    public void Daylight_zero_is_false_and_trailing_bytes_are_ignored()
    {
        var payload = new List<byte>(BuildPayload(true, daylight: 0)) { 0xAA, 0xBB }.ToArray();

        var snapshot = MetadataDecoder.Decode(payload);

        snapshot.DaylightSaving.Should().BeFalse();
        snapshot.Devices.Should().ContainSingle();
    }

    [Fact]
    public void Unknown_device_type_becomes_invalid_but_keeps_its_numeric_code()
    {
        var payload = BuildPayload(withVersion2Fields: true);
        payload[15] = 0;

        var snapshot = MetadataDecoder.Decode(payload);

        snapshot.Devices[0].DeviceType.Should().Be(DeviceType.Invalid);
        snapshot.Devices[0].NumericDeviceType.Should().Be(0);
    }

    [Fact]
    public void Magic_without_version_byte_is_rejected()
    {
        var act = () => MetadataDecoder.Decode([MetadataDecoder.MagicByte]);

        act.Should().Throw<MalformedMetadataException>()
            .WithMessage("*no version byte*");
    }

    [Fact]
    public void Invalid_base64_is_reported()
    {
        var act = () => MetadataDecoder.Decode("!!!");

        act.Should().Throw<MalformedMetadataException>()
            .WithMessage("*not valid base64*");
    }
}
